using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HgssSave.Core;

public sealed record SemanticChange(string Field, string Before, string After);

public sealed record PlannedOperation(
    PatchOperation Operation,
    string TargetKey,
    string FieldLabel,
    SymbolEntry? Symbol,
    SymbolResolution Resolution,
    string RequestedText,
    string OriginalText,
    string? ExpectText,
    bool PreconditionOk,
    bool ChangesValue,
    IReadOnlyList<string> Notes);

/// <summary>
/// A postcondition to re-check after serialization. <see cref="Kind"/>/<see cref="Id"/> are the
/// resolved target so re-evaluation does not need the symbol registry again.
/// </summary>
public sealed record PlannedAssertion(
    PatchAssertion Assertion,
    string ResolvedLabel,
    string ActualText,
    bool CurrentlyTrue,
    SymbolKind? Kind,
    int? Id);

public sealed record PatchPlan(
    PatchDocument Document,
    string InputPath,
    string InputSha256,
    string PatchSha256,
    string RegistryRevision,
    IReadOnlyList<PlannedOperation> Operations,
    IReadOnlyList<PlannedAssertion> Assertions,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> PreconditionFailures,
    IReadOnlyList<string> Warnings,
    bool CanApply);

public sealed record PatchResult(
    PatchPlan Plan,
    byte[] OutputBytes,
    SaveSnapshot Snapshot,
    IReadOnlyList<SemanticChange> SemanticChanges,
    ByteAuditResult Audit,
    StructuralValidation Validation,
    IReadOnlyList<string> PostconditionFailures,
    string OutputSha256,
    int BookkeepingBytes,
    int RequestedBytes);

/// <summary>
/// Turns a patch document into an executable plan against a parsed save, without mutating
/// anything (design §5: "Preconditions operate before mutation").
/// </summary>
public static class PatchPlanner
{
    public static PatchPlan Plan(
        ISaveSession session,
        PatchDocument document,
        SymbolRegistry registry,
        string inputPath,
        byte[] inputBytes,
        byte[] patchBytes,
        bool requireSymbols,
        string? recipeDirectory = null)
    {
        var identity = session.Identity;
        var warnings = new List<string>();
        var failures = new List<string>();
        var conflicts = new List<string>();
        var operations = new List<PlannedOperation>();
        var targets = new Dictionary<string, PlannedOperation>(StringComparer.Ordinal);

        if (document.Game is not null && !string.Equals(document.Game, identity.Game, StringComparison.OrdinalIgnoreCase))
            failures.Add($"patch declares game '{document.Game}' but the save identifies as '{identity.Game}'.");

        var baseSha = HashUtil.Sha256(inputBytes);
        if (document.BaseSha256 is not null && !string.Equals(document.BaseSha256, baseSha, StringComparison.OrdinalIgnoreCase))
            failures.Add($"base_sha256 '{document.BaseSha256}' does not match the input save sha256 '{baseSha}'.");

        if (document.SymbolRegistryRevision is not null
            && !string.Equals(document.SymbolRegistryRevision, registry.Revision, StringComparison.OrdinalIgnoreCase))
            failures.Add($"patch requires registry '{document.SymbolRegistryRevision}' but the loaded registry is '{registry.Revision}'.");

        foreach (var operation in document.Operations)
        {
            var planned = PlanOperation(session, operation, registry, requireSymbols, warnings, recipeDirectory);
            if (planned is null)
                continue;

            if (planned.PreconditionOk is false)
                failures.Add($"precondition failed for {planned.FieldLabel}: expected {planned.ExpectText}, found {planned.OriginalText}.");

            if (targets.TryGetValue(planned.TargetKey, out var existing))
            {
                if (!string.Equals(existing.RequestedText, planned.RequestedText, StringComparison.Ordinal))
                    conflicts.Add($"conflicting writes to {planned.FieldLabel}: '{existing.RequestedText}' and '{planned.RequestedText}'.");
                else
                    warnings.Add($"duplicate write to {planned.FieldLabel} with the same value; applied once.");
            }
            else
            {
                targets[planned.TargetKey] = planned;
                operations.Add(planned);
            }
        }

        var assertions = new List<PlannedAssertion>();
        foreach (var assertion in document.Assertions)
        {
            var (actual, ok, error, kind, id) = EvaluateAssertion(session, assertion, registry);
            if (error is not null)
            {
                failures.Add(error);
                continue;
            }

            assertions.Add(new PlannedAssertion(assertion, assertion.Field, actual, ok, kind, id));
            if (!ok)
                warnings.Add($"assertion '{assertion.Field}' is not satisfied by the input save (found {actual}); it will be checked after the write.");
        }

        var canApply = failures.Count == 0 && conflicts.Count == 0;
        return new PatchPlan(document, inputPath, baseSha, HashUtil.Sha256(patchBytes), registry.Revision, operations, assertions, conflicts, failures, warnings, canApply);
    }

    private static PlannedOperation? PlanOperation(
        ISaveSession session,
        PatchOperation operation,
        SymbolRegistry registry,
        bool requireSymbols,
        List<string> warnings,
        string? recipeDirectory = null)
    {
        switch (operation)
        {
            case FlagSetOperation flag:
            {
                var lookup = registry.LookupAs(flag.Reference, SymbolKind.PersistentFlag);
                if (lookup.Resolution == SymbolResolution.NotFound)
                    throw HgssException.Symbol("flag-unknown", $"flag.set id '{flag.Reference}' is not in registry {registry.Revision}.");

                if (lookup.Resolution == SymbolResolution.WrongKind)
                {
                    var found = string.Join(", ", lookup.Candidates.Select(c => $"{c.Name} ({SymbolKinds.ToJson(c.Kind)})"));
                    throw HgssException.Unsafe("flag-kind", $"'{flag.Reference}' is not a persistent save flag; candidates: {found}. Temporary flags are not stored in the save's persistent flag array.");
                }

                if (lookup.Resolution == SymbolResolution.Ambiguous)
                    throw HgssException.Symbol("flag-ambiguous", $"'{flag.Reference}' matches multiple registry entries: {string.Join(", ", lookup.Candidates.Select(c => $"{c.Name} ({SymbolKinds.ToJson(c.Kind)}, id {c.Id})"))}.");

                int flagId;
                SymbolEntry? symbol;
                if (lookup.Resolution == SymbolResolution.NumericUnreviewed)
                {
                    // LookupAs already validated this parses (hex or decimal); Reference holds the decimal form.
                    flagId = int.Parse(lookup.Reference, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);
                    symbol = registry.LookupNumber(flagId, SymbolKind.PersistentFlag).Entry;
                    if (requireSymbols)
                        throw HgssException.Symbol("flag-numeric", $"numeric flag id {flagId} has no matching persistent-flag symbol; --require-symbols rejects numeric ids.");

                    warnings.Add($"flag id {flagId} is numeric/unreviewed" + (symbol is null ? " (no matching symbol)." : $" (matches {symbol.Name})."));
                }
                else
                {
                    symbol = lookup.Entry!;
                    flagId = symbol.Id;
                }

                if (flagId < 1 || flagId >= session.FlagCount)
                    throw HgssException.Precondition("flag-range", $"flag id {flagId} is outside the persistent range 1..{session.FlagCount - 1}. Flag 0 (FLAG_NOTHING) is ignored by the game's Save_VarsFlags_GetFlagAddr.");

                session.TryGetFlag(flagId, out var current);
                var ok = flag.Expect is null || flag.Expect.Value == current;
                return new PlannedOperation(
                    operation,
                    $"flag:{flagId}",
                    symbol is null ? $"flag.{flagId}" : $"flag.{symbol.Name}",
                    symbol,
                    lookup.Resolution,
                    flag.Value ? "true" : "false",
                    current ? "true" : "false",
                    flag.Expect is null ? null : (flag.Expect.Value ? "true" : "false"),
                    ok,
                    flag.Value != current,
                    []);
            }

            case VariableSetOperation variable:
            {
                var lookup = registry.LookupAs(variable.Reference, SymbolKind.SaveWork);
                if (lookup.Resolution == SymbolResolution.NotFound)
                    throw HgssException.Symbol("variable-unknown", $"variable.set id '{variable.Reference}' is not in registry {registry.Revision}.");

                if (lookup.Resolution == SymbolResolution.WrongKind)
                {
                    var found = string.Join(", ", lookup.Candidates.Select(c => $"{c.Name} ({SymbolKinds.ToJson(c.Kind)}, id {c.Id})"));
                    throw HgssException.Unsafe("variable-kind", $"'{variable.Reference}' is not a save-work variable; candidates: {found}. Temporary vars (VAR_TEMP_*) and special vars (VAR_SPECIAL_*) are not persistent save fields.");
                }

                if (lookup.Resolution == SymbolResolution.Ambiguous)
                    throw HgssException.Symbol("variable-ambiguous", $"'{variable.Reference}' matches multiple save-work entries: {string.Join(", ", lookup.Candidates.Select(c => $"{c.Name} (id {c.Id})"))}.");

                int varId;
                SymbolEntry? symbol;
                if (lookup.Resolution == SymbolResolution.NumericUnreviewed)
                {
                    // LookupAs already validated this parses (hex or decimal); Reference holds the decimal form.
                    varId = int.Parse(lookup.Reference, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);
                    symbol = registry.LookupNumber(varId, SymbolKind.SaveWork).Entry;
                    if (requireSymbols)
                        throw HgssException.Symbol("variable-numeric", $"numeric variable id {varId} has no matching save-work symbol; --require-symbols rejects numeric ids.");

                    warnings.Add($"variable id {varId} is numeric/unreviewed" + (symbol is null ? " (no matching symbol)." : $" (matches {symbol.Name})."));
                }
                else
                {
                    symbol = lookup.Entry!;
                    varId = symbol.Id;
                }

                if (!session.TryGetVar(varId, out var current))
                    throw HgssException.Precondition("variable-range", $"variable id 0x{varId:X4} is not a save-work slot (valid ids are 0x{session.WorkBaseId:X4}..0x{session.WorkBaseId + session.WorkCount - 1:X4}).");

                var ok = variable.Expect is null || variable.Expect.Value == current;
                return new PlannedOperation(
                    operation,
                    $"var:{varId}",
                    symbol is null ? $"variable.0x{varId:X4}" : $"variable.{symbol.Name}",
                    symbol,
                    lookup.Resolution,
                    variable.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    current.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    variable.Expect?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ok,
                    variable.Value != current,
                    []);
            }

            case MoneySetOperation money:
            {
                if (money.Value > session.Trainer.MaxMoney)
                    throw HgssException.Precondition("money-range", $"money {money.Value} exceeds the HGSS maximum {session.Trainer.MaxMoney}.");

                var current = session.Trainer.Money;
                var ok = money.Expect is null || money.Expect.Value == current;
                return new PlannedOperation(
                    operation,
                    "money",
                    "trainer.money",
                    null,
                    SymbolResolution.Resolved,
                    money.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    current.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    money.Expect?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ok,
                    money.Value != current,
                    []);
            }

            case BagQuantitySetOperation quantity:
            {
                var lookup = registry.LookupAs(quantity.Reference, SymbolKind.Item);
                if (lookup.Resolution == SymbolResolution.NotFound)
                    throw HgssException.Symbol("item-unknown", $"inventory.quantity.set item '{quantity.Reference}' is not in registry {registry.Revision}.");

                if (lookup.Resolution == SymbolResolution.Ambiguous)
                    throw HgssException.Symbol("item-ambiguous", $"'{quantity.Reference}' matches multiple item entries: {string.Join(", ", lookup.Candidates.Select(c => $"{c.Name} (id {c.Id})"))}.");

                int itemId;
                SymbolEntry? symbol;
                if (lookup.Resolution == SymbolResolution.NumericUnreviewed)
                {
                    // LookupAs already validated this parses (hex or decimal); Reference holds the decimal form.
                    itemId = int.Parse(lookup.Reference, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);
                    symbol = registry.LookupNumber(itemId, SymbolKind.Item).Entry;
                    if (requireSymbols)
                        throw HgssException.Symbol("item-numeric", $"numeric item id {itemId} has no matching item symbol; --require-symbols rejects numeric ids.");

                    warnings.Add($"item id {itemId} is numeric/unreviewed" + (symbol is null ? " (no matching symbol)." : $" (matches {symbol.Name})."));
                }
                else
                {
                    symbol = lookup.Entry!;
                    itemId = symbol.Id;
                }

                var found = session.TryGetBagQuantity(itemId, out var current, out _);
                if (!found && quantity.Quantity > 0 && !session.IsPocketableItem(itemId))
                    throw HgssException.Unsafe("item-pocket", $"item {itemId} ({symbol?.Name ?? "unnamed"}) is not carried in any HGSS pocket, so it cannot be written to the bag.");

                var ok = quantity.Expect is null || quantity.Expect.Value == (found ? current : 0);
                return new PlannedOperation(
                    operation,
                    $"bag:{itemId}",
                    symbol is null ? $"bag.{itemId}" : $"bag.{symbol.Name}",
                    symbol,
                    lookup.Resolution,
                    quantity.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (found ? current : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    quantity.Expect?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ok,
                    (found ? current : 0) != quantity.Quantity,
                    []);
            }

            case PartyImportOperation party:
            {
                var resolved = ResolvePkFile(party.File, recipeDirectory);
                var preview = PreviewPk(session, resolved);
                if (party.ExpectSpecies is not null && preview.Species != party.ExpectSpecies.Value)
                    throw HgssException.Precondition("pk-expect", $"party slot {party.Slot} import file holds species {preview.Species} but expected {party.ExpectSpecies.Value}.");
                var current = session.PartySpecies(party.Slot);
                return new PlannedOperation(
                    operation,
                    $"party:{party.Slot}",
                    $"party.{party.Slot}.species",
                    null,
                    SymbolResolution.Resolved,
                    preview.Species.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    current.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    party.ExpectSpecies?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    party.ExpectSpecies is null || preview.Species == party.ExpectSpecies.Value,
                    current != preview.Species,
                    [$"pk file: {resolved} species {preview.Species} {preview.SpeciesName} lv{preview.Level} checksum-valid:{preview.ChecksumValid}"]);
            }

            case PartyClearOperation clear:
            {
                if ((uint)clear.Slot >= 6)
                    throw HgssException.Precondition("party-range", $"Party slot {clear.Slot} is outside 0..5.");
                var current = session.PartySpecies(clear.Slot);
                return new PlannedOperation(
                    operation,
                    $"party:{clear.Slot}",
                    $"party.{clear.Slot}.species",
                    null,
                    SymbolResolution.Resolved,
                    "0",
                    current.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    true,
                    current != 0,
                    []);
            }

            case BoxImportOperation box:
            {
                var resolved = ResolvePkFile(box.File, recipeDirectory);
                var preview = PreviewPk(session, resolved);
                if (box.ExpectSpecies is not null && preview.Species != box.ExpectSpecies.Value)
                    throw HgssException.Precondition("pk-expect", $"box {box.Box} slot {box.Slot} import file holds species {preview.Species} but expected {box.ExpectSpecies.Value}.");
                var current = session.BoxSpecies(box.Box, box.Slot);
                return new PlannedOperation(
                    operation,
                    $"box:{box.Box}:{box.Slot}",
                    $"box.{box.Box}.{box.Slot}.species",
                    null,
                    SymbolResolution.Resolved,
                    preview.Species.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    current.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    box.ExpectSpecies?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    box.ExpectSpecies is null || preview.Species == box.ExpectSpecies.Value,
                    current != preview.Species,
                    [$"pk file: {resolved} species {preview.Species} {preview.SpeciesName} lv{preview.Level} checksum-valid:{preview.ChecksumValid}"]);
            }

            case BoxClearOperation clear:
            {
                var current = session.BoxSpecies(clear.Box, clear.Slot);
                return new PlannedOperation(
                    operation,
                    $"box:{clear.Box}:{clear.Slot}",
                    $"box.{clear.Box}.{clear.Slot}.species",
                    null,
                    SymbolResolution.Resolved,
                    "0",
                    current.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    true,
                    current != 0,
                    []);
            }

            default:
                throw HgssException.BadArguments("patch-unknown-op", $"Unsupported operation '{operation.OpName}'.");
        }
    }

    private static string ResolvePkFile(string file, string? recipeDirectory)
    {
        if (Path.IsPathRooted(file))
            return file;
        if (!string.IsNullOrEmpty(recipeDirectory))
            return Path.GetFullPath(Path.Combine(recipeDirectory, file));
        return Path.GetFullPath(file);
    }

    private static PkPreview PreviewPk(ISaveSession session, string resolved)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(resolved);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw HgssException.BadArguments("pk-file", $"Cannot read pk file '{resolved}': {e.Message}");
        }

        try
        {
            return session.PreviewPkFile(bytes);
        }
        catch (HgssException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw HgssException.BadArguments("pk-file", $"Invalid pk file '{resolved}': {e.Message}");
        }
    }

    /// <summary>
    /// Evaluates an assertion against a session. Returns the resolved target category and id so
    /// callers can re-check the same field later without the registry.
    /// </summary>
    public static (string Actual, bool Ok, string? Error, SymbolKind? Kind, int? Id) EvaluateAssertion(
        ISaveSession session,
        PatchAssertion assertion,
        SymbolRegistry? registry)
    {
        switch (assertion)
        {
            case FlagAssertion flag:
            {
                var (id, error) = ResolveReference(registry, flag.Reference, SymbolKind.PersistentFlag, flag.Field);
                if (error is not null)
                    return ("", false, error, null, null);

                if (!session.TryGetFlag(id!.Value, out var value))
                    return ("", false, $"assertion field '{flag.Field}': flag id {id} is out of range.", null, null);

                return (value ? "true" : "false", value == flag.Value, null, SymbolKind.PersistentFlag, id);
            }
            case VariableAssertion variable:
            {
                var (id, error) = ResolveReference(registry, variable.Reference, SymbolKind.SaveWork, variable.Field);
                if (error is not null)
                    return ("", false, error, null, null);

                if (!session.TryGetVar(id!.Value, out var value))
                    return ("", false, $"assertion field '{variable.Field}': variable id {id} is out of range.", null, null);

                return (value.ToString(System.Globalization.CultureInfo.InvariantCulture), value == variable.Value, null, SymbolKind.SaveWork, id);
            }
            case BagAssertion bag:
            {
                var (id, error) = ResolveReference(registry, bag.Reference, SymbolKind.Item, bag.Field);
                if (error is not null)
                    return ("", false, error, null, null);

                var found = session.TryGetBagQuantity(id!.Value, out var quantity, out _);
                var actual = found ? quantity : 0;
                return (actual.ToString(System.Globalization.CultureInfo.InvariantCulture), actual == bag.Quantity, null, SymbolKind.Item, id);
            }
            case MoneyAssertion money:
                return (session.Trainer.Money.ToString(System.Globalization.CultureInfo.InvariantCulture), session.Trainer.Money == money.Value, null, null, null);
            case CoinsAssertion coins:
                return (session.Trainer.Coins.ToString(System.Globalization.CultureInfo.InvariantCulture), session.Trainer.Coins == coins.Value, null, null, null);
            case JohtoBadgesAssertion badges:
                return (session.Trainer.JohtoBadges.ToString(System.Globalization.CultureInfo.InvariantCulture), session.Trainer.JohtoBadges == badges.Value, null, null, null);
            case KantoBadgesAssertion badges:
                return (session.Trainer.KantoBadges.ToString(System.Globalization.CultureInfo.InvariantCulture), session.Trainer.KantoBadges == badges.Value, null, null, null);
            case PartyCountAssertion party:
            {
                var count = session.Snapshot().Party.Count(p => p.Present);
                return (count.ToString(System.Globalization.CultureInfo.InvariantCulture), count == party.Value, null, null, null);
            }
            case PartySpeciesAssertion species:
            {
                int actual;
                try
                {
                    actual = session.PartySpecies(species.Slot);
                }
                catch (HgssException e)
                {
                    return ("", false, $"assertion field '{species.Field}': {e.Message}", null, null);
                }

                return (actual.ToString(System.Globalization.CultureInfo.InvariantCulture), actual == species.Value, null, null, null);
            }
            case BoxSpeciesAssertion species:
            {
                int actual;
                try
                {
                    actual = session.BoxSpecies(species.Box, species.Slot);
                }
                catch (HgssException e)
                {
                    return ("", false, $"assertion field '{species.Field}': {e.Message}", null, null);
                }

                return (actual.ToString(System.Globalization.CultureInfo.InvariantCulture), actual == species.Value, null, null, null);
            }
            case MapAssertion map:
                return (session.Location.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture), session.Location.MapId == map.Value, null, null, null);
            case GameAssertion game:
                return (session.Identity.Game, string.Equals(session.Identity.Game, game.Value, StringComparison.OrdinalIgnoreCase), null, null, null);
            case ChecksumsAssertion checksums:
            {
                var valid = session.ValidateStructural().ChecksumsValid;
                return (valid ? "true" : "false", valid == checksums.Value, null, null, null);
            }
            default:
                return ("", false, $"Unsupported assertion field '{assertion.Field}'.", null, null);
        }
    }

    /// <summary>
    /// Resolves an assertion reference. With a registry, names resolve through it; without one,
    /// the reference must already be numeric (the plan resolves ids up front).
    /// </summary>
    private static (int? Id, string? Error) ResolveReference(SymbolRegistry? registry, string reference, SymbolKind kind, string field)
    {
        if (registry is not null)
        {
            var lookup = registry.LookupAs(reference, kind);
            if (lookup.IsResolved)
                return (lookup.Entry!.Id, null);
            if (lookup.Resolution == SymbolResolution.NumericUnreviewed && SymbolRegistry.TryParseNumber(reference, out var numeric))
                return (numeric, null);
            return (null, $"assertion field '{field}': {lookup.Resolution} for '{reference}'.");
        }

        if (SymbolRegistry.TryParseNumber(reference, out var number))
            return (number, null);

        return (null, $"assertion field '{field}': reference '{reference}' was not resolved to a numeric id.");
    }
}
