using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HgssSave.Core;

/// <summary>
/// Executes a validated plan as one transaction (design §6 steps 3-6, design §9):
/// apply to one in-memory save, serialize, re-parse, check postconditions, then audit
/// every changed byte against the requested writes plus the measured export bookkeeping.
/// Nothing is written to disk here; the CLI commits the bytes atomically.
/// </summary>
public static class PatchRunner
{
    public static PatchResult Apply(
        ISaveSession session,
        PatchPlan plan,
        ISaveFactory factory,
        int maxRanges = 40)
    {
        if (!plan.CanApply)
        {
            var reasons = plan.PreconditionFailures.Concat(plan.Conflicts);
            throw HgssException.Precondition("patch-plan", $"Patch rejected: {string.Join(" ", reasons)}");
        }

        // Bookkeeping baseline: a no-edit export of the same bytes. Whatever this changes is
        // PKHeX export behaviour, not our edit (design §6: "do not promise byte-identical no-op exports").
        IReadOnlyList<int> bookkeeping;
        using (var baseline = factory.OpenBytes((byte[])session.OriginalBytes.Clone(), "bookkeeping-baseline"))
        {
            var baselineExport = baseline.Export().Bytes;
            bookkeeping = ByteAudit.ChangedIndices(session.OriginalBytes, baselineExport);
        }

        var beforeSnapshot = session.Snapshot();
        var requested = new List<int>();
        foreach (var planned in plan.Operations)
        {
            var before = session.CurrentBytes;
            ApplyOperation(session, planned);
            var after = session.CurrentBytes;
            requested.AddRange(ByteAudit.ChangedIndices(before, after));
        }

        var export = session.Export();
        if (!export.ChecksumsValid)
            throw HgssException.Structural("export-checksum", $"Exported save failed checksum verification: {string.Join("; ", export.ChecksumInfo)}");

        using var reparsed = factory.OpenBytes(export.Bytes, "export");
        var validation = reparsed.ValidateStructural();
        if (!validation.Passed)
            throw HgssException.Structural("export-structural", $"Exported save failed structural validation: {string.Join("; ", validation.Failures)}");

        var postconditions = new List<string>();
        foreach (var planned in plan.Operations)
        {
            var actual = ReadTarget(reparsed, planned);
            if (!string.Equals(actual, planned.RequestedText, StringComparison.Ordinal))
                postconditions.Add($"{planned.FieldLabel}: requested {planned.RequestedText} but the exported save reads {actual}.");
        }

        foreach (var assertion in plan.Assertions)
        {
            var actual = EvaluateResolved(reparsed, assertion.Assertion, assertion.Kind, assertion.Id);
            if (actual is null)
            {
                postconditions.Add($"assertion '{assertion.Assertion.Field}' could not be re-read from the exported save.");
                continue;
            }

            if (!string.Equals(actual, ExpectedText(assertion.Assertion), StringComparison.Ordinal))
                postconditions.Add($"assertion '{assertion.ResolvedLabel}' failed on the exported save: expected {ExpectedText(assertion.Assertion)}, found {actual}.");
        }

        var audit = ByteAudit.AuditPatch(session.OriginalBytes, export.Bytes, requested, bookkeeping, factory.KnownRegions, maxRanges);

        var semantic = plan.Operations
            .Where(op => op.ChangesValue)
            .Select(op => new SemanticChange(op.FieldLabel, op.OriginalText, op.RequestedText))
            .ToList();

        var afterSnapshot = reparsed.Snapshot();
        foreach (var change in SaveDiff.FieldChanges(beforeSnapshot, afterSnapshot))
        {
            if (!semantic.Any(s => s.Field == change.Field))
                semantic.Add(change);
        }

        if (postconditions.Count > 0)
            throw HgssException.Structural("postcondition", $"Postconditions failed: {string.Join(" ", postconditions)}");

        if (!audit.Accepted)
            throw HgssException.Internal("byte-audit", $"Byte audit rejected the export: {audit.RejectionReason}");

        return new PatchResult(
            plan,
            export.Bytes,
            afterSnapshot,
            semantic,
            audit,
            validation,
            postconditions,
            HashUtil.Sha256(export.Bytes),
            audit.BookkeepingBytes,
            audit.RequestedBytes);
    }

    private static void ApplyOperation(ISaveSession session, PlannedOperation planned)
    {
        switch (planned.Operation)
        {
            case FlagSetOperation flag:
            {
                var id = TargetId(planned, "flag");
                session.SetFlag(id, flag.Value);
                break;
            }
            case VariableSetOperation variable:
            {
                var id = TargetId(planned, "var");
                session.SetVar(id, variable.Value);
                break;
            }
            case MoneySetOperation money:
                session.SetMoney(money.Value);
                break;
            case BagQuantitySetOperation quantity:
            {
                var id = TargetId(planned, "bag");
                session.SetBagQuantity(id, quantity.Quantity);
                break;
            }
            case PartyImportOperation party:
            {
                var bytes = ReadPkFile(planned, party.File);
                session.ImportPartySlot(party.Slot, bytes, party.UpdateDex);
                break;
            }
            case PartyClearOperation clear:
                session.ClearPartySlot(clear.Slot);
                break;
            case BoxImportOperation box:
            {
                var bytes = ReadPkFile(planned, box.File);
                session.ImportBoxSlot(box.Box, box.Slot, bytes, box.UpdateDex);
                break;
            }
            case BoxClearOperation clear:
                session.ClearBoxSlot(clear.Box, clear.Slot);
                break;
            default:
                throw HgssException.BadArguments("patch-unknown-op", $"Unsupported operation '{planned.Operation.OpName}'.");
        }
    }

    private static byte[] ReadPkFile(PlannedOperation planned, string file)
    {
        var resolved = file;
        if (!Path.IsPathRooted(resolved))
            resolved = Path.GetFullPath(resolved);
        // Planner notes carry the resolved absolute path in Notes[0] as "pk file: <path> ...".
        if (planned.Notes.Count > 0 && planned.Notes[0].StartsWith("pk file: ", StringComparison.Ordinal))
        {
            var remainder = planned.Notes[0]["pk file: ".Length..];
            var end = remainder.IndexOf(" species ", StringComparison.Ordinal);
            if (end > 0)
                resolved = remainder[..end];
        }

        try
        {
            return File.ReadAllBytes(resolved);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw HgssException.BadArguments("pk-file", $"Cannot read pk file '{resolved}': {e.Message}");
        }
    }

    private static int TargetId(PlannedOperation planned, string prefix)
    {
        if (!planned.TargetKey.StartsWith(prefix + ":", StringComparison.Ordinal))
            throw HgssException.Internal("plan-target", $"Planned operation target '{planned.TargetKey}' does not match operation '{planned.Operation.OpName}'.");

        var text = planned.TargetKey[(prefix.Length + 1)..];
        if (!int.TryParse(text, out var id))
            throw HgssException.Internal("plan-target", $"Planned target id '{text}' is not an integer.");

        return id;
    }

    private static string? ReadTarget(ISaveSession session, PlannedOperation planned)
    {
        switch (planned.Operation)
        {
            case FlagSetOperation:
            {
                var id = TargetId(planned, "flag");
                if (!session.TryGetFlag(id, out var value))
                    return null;
                return value ? "true" : "false";
            }
            case VariableSetOperation:
            {
                var id = TargetId(planned, "var");
                if (!session.TryGetVar(id, out var value))
                    return null;
                return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            case MoneySetOperation:
                return session.Trainer.Money.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case BagQuantitySetOperation:
            {
                var id = TargetId(planned, "bag");
                var found = session.TryGetBagQuantity(id, out var quantity, out _);
                return (found ? quantity : 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            case PartyImportOperation party:
                return session.PartySpecies(party.Slot).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case PartyClearOperation clear:
                return session.PartySpecies(clear.Slot).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case BoxImportOperation box:
                return session.BoxSpecies(box.Box, box.Slot).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case BoxClearOperation clear:
                return session.BoxSpecies(clear.Box, clear.Slot).ToString(System.Globalization.CultureInfo.InvariantCulture);
            default:
                return null;
        }
    }

    /// <summary>
    /// Re-reads an already-resolved assertion target. Null means the field is unreadable, which
    /// the caller treats as a failed postcondition rather than a mismatch.
    /// </summary>
    private static string? EvaluateResolved(ISaveSession session, PatchAssertion assertion, SymbolKind? kind, int? id)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        switch (assertion)
        {
            case FlagAssertion:
                return kind == SymbolKind.PersistentFlag && id is not null && session.TryGetFlag(id.Value, out var flag)
                    ? (flag ? "true" : "false")
                    : null;
            case VariableAssertion:
                return kind == SymbolKind.SaveWork && id is not null && session.TryGetVar(id.Value, out var value)
                    ? value.ToString(invariant)
                    : null;
            case BagAssertion:
                return kind == SymbolKind.Item && id is not null
                    ? (session.TryGetBagQuantity(id.Value, out var quantity, out _) ? quantity : 0).ToString(invariant)
                    : null;
            case MoneyAssertion:
                return session.Trainer.Money.ToString(invariant);
            case CoinsAssertion:
                return session.Trainer.Coins.ToString(invariant);
            case JohtoBadgesAssertion:
                return session.Trainer.JohtoBadges.ToString(invariant);
            case KantoBadgesAssertion:
                return session.Trainer.KantoBadges.ToString(invariant);
            case PartyCountAssertion:
                return session.Snapshot().Party.Count(p => p.Present).ToString(invariant);
            case PartySpeciesAssertion species:
                return session.PartySpecies(species.Slot).ToString(invariant);
            case BoxSpeciesAssertion species:
                return session.BoxSpecies(species.Box, species.Slot).ToString(invariant);
            case MapAssertion:
                return session.Location.MapId.ToString(invariant);
            case GameAssertion:
                return session.Identity.Game;
            case ChecksumsAssertion checksums:
                return session.ValidateStructural().ChecksumsValid ? "true" : "false";
            default:
                return null;
        }
    }

    private static string ExpectedText(PatchAssertion assertion)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return assertion switch
        {
            FlagAssertion flag => flag.Value ? "true" : "false",
            VariableAssertion variable => variable.Value.ToString(invariant),
            BagAssertion bag => bag.Quantity.ToString(invariant),
            MoneyAssertion money => money.Value.ToString(invariant),
            CoinsAssertion coins => coins.Value.ToString(invariant),
            JohtoBadgesAssertion badges => badges.Value.ToString(invariant),
            KantoBadgesAssertion badges => badges.Value.ToString(invariant),
            PartyCountAssertion party => party.Value.ToString(invariant),
            PartySpeciesAssertion species => species.Value.ToString(invariant),
            BoxSpeciesAssertion species => species.Value.ToString(invariant),
            MapAssertion map => map.Value.ToString(invariant),
            GameAssertion game => game.Value,
            ChecksumsAssertion checksums => checksums.Value ? "true" : "false",
            _ => assertion.Field,
        };
    }
}
