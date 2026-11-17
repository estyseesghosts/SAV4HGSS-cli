using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HgssSave.Core;

/// <summary>
/// Deterministic JSON reports (design §4 output shape, §12 deterministic stdout JSON).
/// Property order is fixed by construction, values come only from the parsed save or the
/// registry, and no wall-clock timestamps are emitted.
/// </summary>
public static class ReportWriter
{
    public const string ReportSchema = "hgss-save.report/v1";

    public static string Serialize(Action<Utf8JsonWriter> write, bool indent)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indent }))
        {
            write(writer);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static void WriteVersion(
        Utf8JsonWriter writer,
        string cliVersion,
        string libraryVersion,
        SymbolRegistry? registry,
        IReadOnlyList<string> commands)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "version");
        writer.WriteString("cli_version", cliVersion);
        WriteSources(writer, libraryVersion, registry);
        writer.WriteString("patch_schema", PatchDocument.SchemaId);
        writer.WriteString("symbols_schema", SymbolRegistry.SchemaId);
        WriteStringList(writer, "commands", commands);
        WriteWarnings(writer, registry is null
            ? ["symbol registry not loaded; named flag/variable/item lookup is unavailable"]
            : []);
        writer.WriteEndObject();
    }

    public static void WriteInspect(
        Utf8JsonWriter writer,
        string operation,
        string path,
        byte[] bytes,
        SaveSnapshot snapshot,
        StructuralValidation validation,
        SymbolRegistry? registry,
        string libraryVersion,
        IReadOnlyList<string> warnings,
        int flagsListLimit)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", operation);
        WriteInput(writer, path, bytes);
        WriteFormat(writer, snapshot.Identity, registry);
        WriteSources(writer, libraryVersion, registry);
        WriteValidation(writer, validation);
        WriteTrainer(writer, snapshot.Trainer);
        WriteLocation(writer, snapshot.Location);
        WritePartition(writer, snapshot.Preflight);
        WriteParty(writer, snapshot.Party);
        WriteBag(writer, snapshot.Bag, snapshot.Pouches);
        WriteBoxes(writer, snapshot.Boxes);
        WriteDex(writer, snapshot.Dex);
        WriteEventCounts(writer, snapshot, registry, flagsListLimit);
        WriteWarnings(writer, warnings);
        writer.WriteEndObject();
    }

    public static void WriteValidate(
        Utf8JsonWriter writer,
        string path,
        byte[] bytes,
        SaveSnapshot snapshot,
        StructuralValidation validation,
        SymbolRegistry? registry,
        string libraryVersion,
        IReadOnlyList<string> warnings)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "validate");
        WriteInput(writer, path, bytes);
        WriteFormat(writer, snapshot.Identity, registry);
        WriteSources(writer, libraryVersion, registry);
        WriteValidation(writer, validation);
        WritePartition(writer, snapshot.Preflight);
        WriteWarnings(writer, warnings);
        writer.WriteEndObject();
    }

    public static void WriteFlags(
        Utf8JsonWriter writer,
        string path,
        byte[] bytes,
        SaveSnapshot snapshot,
        SymbolRegistry registry,
        IReadOnlyList<int> setFlagIds,
        string libraryVersion,
        int limit)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "flags.list");
        WriteInput(writer, path, bytes);
        WriteSources(writer, libraryVersion, registry);
        writer.WriteStartObject("flags");
        writer.WriteNumber("count", snapshot.FlagsSet);
        writer.WriteNumber("capacity", snapshot.FlagCount);
        writer.WriteNumber("returned", Math.Min(setFlagIds.Count, limit));
        writer.WriteBoolean("truncated", setFlagIds.Count > limit);
        writer.WriteStartArray("set");
        foreach (var id in setFlagIds.Take(limit))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("hex", $"0x{id:X}");
            writer.WriteNullableString("name", registry.NameFor(SymbolKind.PersistentFlag, id));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        WriteWarnings(writer, []);
        writer.WriteEndObject();
    }

    public static void WriteVars(
        Utf8JsonWriter writer,
        string path,
        byte[] bytes,
        SaveSnapshot snapshot,
        SymbolRegistry registry,
        IReadOnlyDictionary<int, int> values,
        string libraryVersion,
        int limit)
    {
        var listed = values.OrderBy(p => p.Key).Take(limit).ToList();
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "vars.list");
        WriteInput(writer, path, bytes);
        WriteSources(writer, libraryVersion, registry);
        writer.WriteStartObject("variables");
        writer.WriteNumber("count", snapshot.WorkNonZero);
        writer.WriteNumber("capacity", snapshot.WorkCount);
        writer.WriteNumber("base_id", snapshot.WorkBaseId);
        writer.WriteNumber("returned", listed.Count);
        writer.WriteBoolean("truncated", values.Count > listed.Count);
        writer.WriteStartArray("values");
        foreach (var pair in listed)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", pair.Key);
            writer.WriteString("hex", $"0x{pair.Key:X4}");
            writer.WriteNullableString("name", registry.NameFor(SymbolKind.SaveWork, pair.Key));
            writer.WriteNumber("value", pair.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        WriteWarnings(writer, []);
        writer.WriteEndObject();
    }

    public static void WriteSymbolsLookup(
        Utf8JsonWriter writer,
        SymbolRegistry registry,
        string reference,
        SymbolLookupResult result,
        string libraryVersion)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "symbols.lookup");
        WriteSources(writer, libraryVersion, registry);
        writer.WriteString("reference", reference);
        writer.WriteString("resolution", result.Resolution.ToString().ToLowerInvariant());
        writer.WriteStartArray("matches");
        foreach (var entry in result.Candidates)
            WriteSymbol(writer, entry);

        writer.WriteEndArray();
        WriteWarnings(writer, result.Resolution switch
        {
            SymbolResolution.NotFound => [$"'{reference}' is not defined in {registry.Revision}."],
            SymbolResolution.Ambiguous => [$"'{reference}' matches {result.Candidates.Count} entries; disambiguate with a category filter."],
            SymbolResolution.WrongKind => [$"'{reference}' exists but not in the requested category."],
            _ => [],
        });
        writer.WriteEndObject();
    }

    public static void WriteSymbolsSearch(
        Utf8JsonWriter writer,
        SymbolRegistry registry,
        string query,
        IReadOnlyList<SymbolKind> kinds,
        IReadOnlyList<SymbolEntry> results,
        string libraryVersion,
        int limit)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "symbols.search");
        WriteSources(writer, libraryVersion, registry);
        writer.WriteString("query", query);
        writer.WriteStartArray("kinds");
        foreach (var kind in kinds)
            writer.WriteStringValue(SymbolKinds.ToJson(kind));

        writer.WriteEndArray();
        writer.WriteNumber("limit", limit);
        writer.WriteNumber("returned", results.Count);
        writer.WriteStartArray("matches");
        foreach (var entry in results)
            WriteSymbol(writer, entry);

        writer.WriteEndArray();
        WriteWarnings(writer, []);
        writer.WriteEndObject();
    }

    public static void WriteScriptRefs(
        Utf8JsonWriter writer,
        string symbol,
        string indexPath,
        IReadOnlyList<ScriptRef> refs,
        string libraryVersion,
        int limit)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "script.references");
        writer.WriteString("symbol", symbol);
        writer.WriteString("index", indexPath);
        writer.WriteString("pkhex_core_version", libraryVersion);
        writer.WriteNumber("limit", limit);
        writer.WriteNumber("ref_count", refs.Count);
        writer.WriteBoolean("truncated", refs.Count > limit);
        writer.WriteStartArray("refs");
        foreach (var r in refs.Take(limit))
        {
            writer.WriteStartObject();
            writer.WriteString("symbol", r.Symbol);
            writer.WriteString("access", r.Access);
            writer.WriteString("command", r.Command);
            writer.WriteString("file", r.File);
            writer.WriteNumber("line", r.Line);
            writer.WriteNullableString("context", r.Context);
            writer.WriteString("confidence", r.Confidence);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteWarnings(writer, refs.Count == 0
            ? [$"'{symbol}' has no indexed script references; it may be engine-read, unused, or outside scr_seq."]
            : []);
        writer.WriteEndObject();
    }

    public static void WritePatch(
        Utf8JsonWriter writer,
        PatchPlan plan,
        PatchResult? result,
        string? outputPath,
        bool dryRun,
        string libraryVersion)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", dryRun ? "patch.dry-run" : "patch");
        WriteInput(writer, plan.InputPath, plan.InputSha256);
        writer.WriteString("patch_sha256", plan.PatchSha256);
        var revision = plan.RegistryRevision;
        var at = revision.LastIndexOf('@');
        WriteSources(writer, libraryVersion, at >= 0 ? revision[(at + 1)..] : revision, revision);
        writer.WriteStartObject("patch");
        writer.WriteString("schema_id", plan.Document.Schema);
        writer.WriteNullableString("game", plan.Document.Game);
        writer.WriteNullableString("base_sha256", plan.Document.BaseSha256);
        writer.WriteNullableString("symbol_registry", plan.Document.SymbolRegistryRevision);
        writer.WriteEndObject();
        writer.WriteStartArray("operations");
        foreach (var op in plan.Operations)
        {
            writer.WriteStartObject();
            writer.WriteString("op", op.Operation.OpName);
            writer.WriteString("field", op.FieldLabel);
            writer.WriteString("resolution", op.Resolution.ToString().ToLowerInvariant());
            if (op.Symbol is not null)
            {
                writer.WriteNumber("id", op.Symbol.Id);
                writer.WriteString("hex", op.Symbol.Hex);
                writer.WriteString("symbol_kind", SymbolKinds.ToJson(op.Symbol.Kind));
                writer.WriteString("symbol_source", op.Symbol.Source);
                writer.WriteNumber("symbol_line", op.Symbol.Line);
            }

            writer.WriteString("from", op.OriginalText);
            writer.WriteString("to", op.RequestedText);
            writer.WriteNullableString("expect", op.ExpectText);
            writer.WriteBoolean("precondition_ok", op.PreconditionOk);
            writer.WriteBoolean("changes_value", op.ChangesValue);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("assertions");
        foreach (var assertion in plan.Assertions)
        {
            writer.WriteStartObject();
            writer.WriteString("field", assertion.ResolvedLabel);
            writer.WriteString("equals", Expected(assertion.Assertion));
            writer.WriteString("input_value", assertion.ActualText);
            writer.WriteBoolean("input_satisfied", assertion.CurrentlyTrue);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteStringList(writer, "conflicts", plan.Conflicts);
        WriteStringList(writer, "precondition_failures", plan.PreconditionFailures);

        if (result is not null)
        {
            writer.WriteStartObject("output");
            writer.WriteNullableString("path", outputPath);
            writer.WriteString("sha256", result.OutputSha256);
            writer.WriteNumber("bytes", result.OutputBytes.Length);
            writer.WriteBoolean("dry_run", dryRun);
            writer.WriteEndObject();

            writer.WriteStartArray("semantic_diff");
            foreach (var change in result.SemanticChanges)
            {
                writer.WriteStartObject();
                writer.WriteString("field", change.Field);
                writer.WriteString("before", change.Before);
                writer.WriteString("after", change.After);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WriteByteAudit(writer, result.Audit, result.RequestedBytes, result.BookkeepingBytes);
            WriteValidation(writer, result.Validation);
            WriteStringList(writer, "postcondition_failures", result.PostconditionFailures);
        }

        WriteWarnings(writer, plan.Warnings);
        writer.WriteEndObject();
    }

    public static void WriteDiff(Utf8JsonWriter writer, SaveDiffResult diff)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "diff");
        WriteInput(writer, diff.BeforePath, diff.BeforeSha256);
        writer.WriteStartObject("after");
        writer.WriteString("path", diff.AfterPath);
        writer.WriteString("sha256", diff.AfterSha256);
        writer.WriteString("game", diff.AfterIdentity.Game);
        writer.WriteEndObject();
        writer.WriteBoolean("identical_bytes", diff.SameFile);
        writer.WriteStartArray("fields");
        foreach (var change in diff.Fields)
        {
            writer.WriteStartObject();
            writer.WriteString("field", change.Field);
            writer.WriteString("before", change.Before);
            writer.WriteString("after", change.After);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartObject("flags");
        writer.WriteNumber("changed", diff.FlagsChanged);
        writer.WriteBoolean("truncated", diff.FlagsChanged > diff.Flags.Count);
        writer.WriteStartArray("changes");
        foreach (var change in diff.Flags)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", change.Id);
            writer.WriteString("hex", $"0x{change.Id:X}");
            writer.WriteNullableString("name", change.Name);
            writer.WriteString("direction", change.Direction);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteStartObject("variables");
        writer.WriteNumber("changed", diff.VarsChanged);
        writer.WriteBoolean("truncated", diff.VarsChanged > diff.Vars.Count);
        writer.WriteStartArray("changes");
        foreach (var change in diff.Vars)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", change.Id);
            writer.WriteString("hex", $"0x{change.Id:X4}");
            writer.WriteNullableString("name", change.Name);
            writer.WriteNumber("before", change.Before);
            writer.WriteNumber("after", change.After);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteStartArray("bag");
        foreach (var change in diff.Bag)
        {
            writer.WriteStartObject();
            writer.WriteNumber("item", change.ItemId);
            writer.WriteNullableString("name", change.Name);
            writer.WriteString("pocket", change.Pouch.ToString());
            writer.WriteNumber("before", change.Before);
            writer.WriteNumber("after", change.After);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("party");
        foreach (var change in diff.Party)
        {
            writer.WriteStartObject();
            writer.WriteNumber("slot", change.Slot);
            writer.WriteString("field", change.Field);
            writer.WriteString("before", change.Before);
            writer.WriteString("after", change.After);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteByteAudit(writer, diff.Bytes, diff.Bytes.RequestedBytes, diff.Bytes.BookkeepingBytes);
        WriteWarnings(writer, diff.Warnings);
        writer.WriteEndObject();
    }

    public static void WriteBlank(
        Utf8JsonWriter writer,
        string path,
        byte[] bytes,
        BlankKind kind,
        BlankOptions options,
        string libraryVersion,
        SymbolRegistry? registry,
        IReadOnlyList<string> warnings)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "blank");
        writer.WriteStartObject("output");
        writer.WriteString("path", path);
        writer.WriteString("sha256", HashUtil.Sha256(bytes));
        writer.WriteNumber("bytes", bytes.Length);
        writer.WriteEndObject();
        writer.WriteStartObject("blank");
        writer.WriteString("kind", kind == BlankKind.ErasedFlash ? "erased-flash" : "fresh-structure");
        writer.WriteString("game", options.Game);
        writer.WriteNullableString("trainer_name", options.TrainerName);
        writer.WriteNumber("tid", options.Tid16 ?? 0);
        writer.WriteNumber("sid", options.Sid16 ?? 0);
        writer.WriteNumber("gender", options.Gender ?? 0);
        writer.WriteNumber("language", options.Language ?? 2);
        writer.WriteBoolean("story_verified", false);
        writer.WriteEndObject();
        WriteSources(writer, libraryVersion, registry);
        WriteWarnings(writer, warnings);
        writer.WriteEndObject();
    }

    public static void WriteUnwrap(
        Utf8JsonWriter writer,
        string inputPath,
        ContainerKind container,
        string outputPath,
        byte[] bytes,
        string libraryVersion,
        SymbolRegistry? registry,
        IReadOnlyList<string> warnings)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "unwrap");
        writer.WriteStartObject("input");
        writer.WriteString("path", inputPath);
        writer.WriteString("container", ContainerStrings.ToDisplay(container));
        writer.WriteEndObject();
        writer.WriteStartObject("output");
        writer.WriteString("path", outputPath);
        writer.WriteString("sha256", HashUtil.Sha256(bytes));
        writer.WriteNumber("bytes", bytes.Length);
        writer.WriteEndObject();
        WriteSources(writer, libraryVersion, registry);
        WriteWarnings(writer, warnings);
        writer.WriteEndObject();
    }

    public static void WriteValue(
        Utf8JsonWriter writer,
        string operation,
        string path,
        byte[] bytes,
        string field,
        string value,
        string libraryVersion,
        SymbolRegistry? registry,
        IReadOnlyList<string> warnings)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", operation);
        WriteInput(writer, path, bytes);
        WriteSources(writer, libraryVersion, registry);
        writer.WriteString("field", field);
        writer.WriteString("value", value);
        WriteWarnings(writer, warnings);
        writer.WriteEndObject();
    }

    public static void WriteError(Utf8JsonWriter writer, HgssException error)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "error");
        writer.WriteString("diagnostic", error.DiagnosticId);
        writer.WriteString("message", error.Message);
        writer.WriteNumber("exit_code", (int)error.ExitCode);
        writer.WriteString("exit_name", error.ExitCode.ToString());
        writer.WriteEndObject();
    }

    private static string Expected(PatchAssertion assertion) => assertion switch
    {
        FlagAssertion flag => flag.Value ? "true" : "false",
        VariableAssertion variable => variable.Value.ToString(CultureInfo.InvariantCulture),
        BagAssertion bag => bag.Quantity.ToString(CultureInfo.InvariantCulture),
        MoneyAssertion money => money.Value.ToString(CultureInfo.InvariantCulture),
        CoinsAssertion coins => coins.Value.ToString(CultureInfo.InvariantCulture),
        JohtoBadgesAssertion badges => badges.Value.ToString(CultureInfo.InvariantCulture),
        KantoBadgesAssertion badges => badges.Value.ToString(CultureInfo.InvariantCulture),
        PartyCountAssertion party => party.Value.ToString(CultureInfo.InvariantCulture),
        PartySpeciesAssertion species => species.Value.ToString(CultureInfo.InvariantCulture),
        BoxSpeciesAssertion species => species.Value.ToString(CultureInfo.InvariantCulture),
        DexSeenAssertion species => species.Value.ToString().ToLowerInvariant(),
        DexCaughtAssertion species => species.Value.ToString().ToLowerInvariant(),
        LocationAssertion location => $"{location.Map},{location.X},{location.Y},{location.Z}",
        MapAssertion map => map.Value.ToString(CultureInfo.InvariantCulture),
        GameAssertion game => game.Value,
        ChecksumsAssertion checksums => checksums.Value ? "true" : "false",
        _ => assertion.Field,
    };

    private static void WriteNullableString(this Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }

    private static void WriteSources(Utf8JsonWriter writer, string libraryVersion, SymbolRegistry? registry)
    {
        writer.WriteStartObject("sources");
        writer.WriteString("pkhex_core_version", libraryVersion);
        writer.WriteString("pokeheartgold_commit", registry?.Source.Commit ?? "not-loaded");
        writer.WriteString("symbol_registry", registry?.Revision ?? "not-loaded");
        writer.WriteEndObject();
    }

    private static void WriteSources(Utf8JsonWriter writer, string libraryVersion, string commit, string revision)
    {
        writer.WriteStartObject("sources");
        writer.WriteString("pkhex_core_version", libraryVersion);
        writer.WriteString("pokeheartgold_commit", commit);
        writer.WriteString("symbol_registry", revision);
        writer.WriteEndObject();
    }

    private static void WriteInput(Utf8JsonWriter writer, string path, byte[] bytes) =>
        WriteInput(writer, path, HashUtil.Sha256(bytes));

    private static void WriteInput(Utf8JsonWriter writer, string path, string sha256)
    {
        writer.WriteStartObject("input");
        writer.WriteString("path", path);
        writer.WriteString("sha256", sha256);
        writer.WriteEndObject();
    }

    private static void WriteFormat(Utf8JsonWriter writer, SaveIdentity identity, SymbolRegistry? registry)
    {
        writer.WriteStartObject("format");
        writer.WriteString("family", identity.Family);
        writer.WriteString("game", identity.Game);
        writer.WriteNumber("game_code", identity.GameCode);
        writer.WriteNumber("bytes", identity.Bytes);
        writer.WriteNumber("language_code", identity.Language);
        writer.WriteString("language_name", LanguageName(identity.Language));
        writer.WriteString("region", registry?.Source.Project ?? "unverified");
        writer.WriteEndObject();
    }

    private static void WriteValidation(Utf8JsonWriter writer, StructuralValidation validation)
    {
        writer.WriteStartObject("validation");
        writer.WriteString("structural", validation.Passed ? "pass" : "fail");
        writer.WriteString("semantic", "not-evaluated");
        writer.WriteBoolean("size_ok", validation.SizeOk);
        writer.WriteBoolean("identity_ok", validation.IdentityOk);
        writer.WriteBoolean("checksums_valid", validation.ChecksumsValid);
        writer.WriteBoolean("preflight_ok", validation.PreflightOk);
        WriteStringList(writer, "failures", validation.Failures);
        WriteStringList(writer, "checksum_info", validation.ChecksumInfo);
        writer.WriteEndObject();
    }

    private static void WriteTrainer(Utf8JsonWriter writer, TrainerSnapshot trainer)
    {
        writer.WriteStartObject("trainer");
        writer.WriteString("name", trainer.Name);
        writer.WriteNumber("gender", trainer.Gender);
        writer.WriteNumber("tid", trainer.Tid16);
        writer.WriteNumber("sid", trainer.Sid16);
        writer.WriteNumber("id32", trainer.Id32);
        writer.WriteNumber("money", trainer.Money);
        writer.WriteNumber("money_max", trainer.MaxMoney);
        writer.WriteNumber("coins", trainer.Coins);
        writer.WriteNumber("badges_johto", trainer.JohtoBadges);
        writer.WriteNumber("badges_kanto", trainer.KantoBadges);
        writer.WriteString("play_time", $"{trainer.PlayedHours}:{trainer.PlayedMinutes:00}:{trainer.PlayedSeconds:00}");
        writer.WriteEndObject();
    }

    private static void WriteLocation(Utf8JsonWriter writer, LocationSnapshot location)
    {
        writer.WriteStartObject("location");
        writer.WriteNumber("map_id", location.MapId);
        writer.WriteNullableString("map_symbol", location.MapSymbol);
        writer.WriteNumber("x", location.X);
        writer.WriteNumber("y", location.Y);
        writer.WriteNumber("secondary_x", location.SecondaryX);
        writer.WriteNumber("secondary_y", location.SecondaryY);
        writer.WriteNumber("z", location.Z);
        writer.WriteEndObject();
    }

    private static void WritePartition(Utf8JsonWriter writer, PreflightReport preflight)
    {
        writer.WriteStartObject("blocks");
        writer.WriteString("active_general", preflight.ActiveGeneral.ToString());
        writer.WriteString("active_storage", preflight.ActiveStorage.ToString());
        writer.WriteStartArray("copies");
        foreach (var copy in preflight.Copies)
        {
            writer.WriteStartObject();
            writer.WriteString("block", copy.Block);
            writer.WriteString("partition", copy.Partition.ToString());
            writer.WriteNumber("offset", copy.Offset);
            writer.WriteNumber("length", copy.Length);
            writer.WriteBoolean("initialized", copy.Initialized);
            writer.WriteBoolean("crc_valid", copy.CrcValid);
            writer.WriteString("crc_saved", $"0x{copy.CrcSaved:X4}");
            writer.WriteString("crc_computed", $"0x{copy.CrcComputed:X4}");
            writer.WriteNumber("counter_or_key", copy.CounterOrKey);
            writer.WriteNumber("counter_secondary", copy.CounterSecondary);
            writer.WriteString("magic", $"0x{copy.Magic:X8}");
            writer.WriteNumber("recorded_size", copy.RecordedSize);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteStringList(writer, "warnings", preflight.Warnings);
        writer.WriteEndObject();
    }

    private static void WriteParty(Utf8JsonWriter writer, IReadOnlyList<PartySlotInfo> party)
    {
        writer.WriteStartObject("party");
        writer.WriteNumber("count", party.Count(p => p.Present));
        writer.WriteStartArray("slots");
        foreach (var slot in party)
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", slot.Index);
            writer.WriteBoolean("present", slot.Present);
            if (slot.Present)
            {
                writer.WriteString("nickname", slot.Nickname);
                writer.WriteNumber("species", slot.Species);
                writer.WriteString("species_name", slot.SpeciesName);
                writer.WriteNumber("form", slot.Form);
                writer.WriteNumber("level", slot.Level);
                writer.WriteBoolean("egg", slot.IsEgg);
                writer.WriteNumber("held_item", slot.HeldItem);
                writer.WriteNullableString("held_item_name", slot.HeldItemName);
                writer.WriteNumber("tid", slot.Tid16);
                writer.WriteNumber("sid", slot.Sid16);
                writer.WriteNumber("experience", slot.Experience);
                writer.WriteNumber("happiness", slot.Happiness);
                writer.WriteStartArray("moves");
                foreach (var move in slot.Moves)
                    writer.WriteNumberValue(move);

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteBag(Utf8JsonWriter writer, IReadOnlyList<BagEntry> bag, IReadOnlyList<PouchCapacity> pouches)
    {
        writer.WriteStartObject("bag");
        writer.WriteNumber("entries", bag.Count(e => e.Quantity > 0));
        writer.WriteStartArray("pockets");
        foreach (var pouch in pouches)
        {
            writer.WriteStartObject();
            writer.WriteString("pocket", pouch.Pouch.ToString());
            writer.WriteNumber("slots", pouch.Slots);
            writer.WriteNumber("occupied", pouch.Occupied);
            writer.WriteNumber("max_quantity", pouch.MaxQuantityPerItem);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("items");
        foreach (var entry in bag.Where(e => e.Quantity > 0).OrderBy(e => e.Pouch).ThenBy(e => e.Slot))
        {
            writer.WriteStartObject();
            writer.WriteString("pocket", entry.Pouch.ToString());
            writer.WriteNumber("slot", entry.Slot);
            writer.WriteNumber("item", entry.ItemId);
            writer.WriteNullableString("name", entry.ItemName);
            writer.WriteNumber("quantity", entry.Quantity);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteBoxes(Utf8JsonWriter writer, IReadOnlyList<BoxSlotInfo> boxes)
    {
        writer.WriteStartObject("boxes");
        writer.WriteNumber("stored", boxes.Count(b => b.Present));
        writer.WriteStartArray("nonempty_boxes");
        foreach (var group in boxes.Where(b => b.Present).GroupBy(b => b.Box).OrderBy(g => g.Key))
        {
            writer.WriteStartObject();
            writer.WriteNumber("box", group.Key);
            writer.WriteNumber("stored", group.Count());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDex(Utf8JsonWriter writer, DexSummary dex)
    {
        writer.WriteStartObject("dex");
        writer.WriteNumber("seen", dex.Seen);
        writer.WriteNumber("caught", dex.Caught);
        writer.WriteNumber("total_slots", dex.Total);
        writer.WriteEndObject();
    }

    private static void WriteEventCounts(
        Utf8JsonWriter writer,
        SaveSnapshot snapshot,
        SymbolRegistry? registry,
        int limit)
    {
        writer.WriteStartObject("event_state");
        writer.WriteNumber("flags_set", snapshot.FlagsSet);
        writer.WriteNumber("flags_capacity", snapshot.FlagCount);
        writer.WriteNumber("variables_nonzero", snapshot.WorkNonZero);
        writer.WriteNumber("variables_capacity", snapshot.WorkCount);
        writer.WriteNumber("variables_base_id", snapshot.WorkBaseId);
        writer.WriteBoolean("named", registry is not null);
        writer.WriteNumber("list_limit", limit);
        writer.WriteEndObject();
    }

    private static void WriteByteAudit(
        Utf8JsonWriter writer,
        ByteAuditResult audit,
        int requested,
        int bookkeeping)
    {
        writer.WriteStartObject("byte_diff");
        writer.WriteNumber("changed_bytes", audit.ChangedBytes);
        writer.WriteNumber("requested_bytes", requested);
        writer.WriteNumber("bookkeeping_bytes", bookkeeping);
        writer.WriteNumber("checksum_bytes", audit.ChecksumBytes);
        writer.WriteNumber("unclassified_bytes", audit.UnclassifiedBytes);
        writer.WriteBoolean("accepted", audit.Accepted);
        writer.WriteNullableString("rejection", audit.RejectionReason);
        writer.WriteStartArray("ranges");
        foreach (var range in audit.Ranges)
        {
            writer.WriteStartObject();
            writer.WriteString("partition", range.Partition.ToString());
            writer.WriteString("start", $"0x{range.Start:X5}");
            writer.WriteString("end", $"0x{range.End:X5}");
            writer.WriteNumber("length", range.Length);
            writer.WriteString("before", range.BeforeHex);
            writer.WriteString("after", range.AfterHex);
            writer.WriteString("category", range.Category);
            writer.WriteString("region", range.Region);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSymbol(Utf8JsonWriter writer, SymbolEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteString("name", entry.Name);
        writer.WriteString("kind", SymbolKinds.ToJson(entry.Kind));
        writer.WriteNumber("id", entry.Id);
        writer.WriteString("hex", entry.Hex);
        writer.WriteBoolean("writable", entry.Writable);
        writer.WriteString("source", entry.Source);
        writer.WriteNumber("line", entry.Line);
        writer.WriteString("confidence", entry.Confidence);
        WriteStringList(writer, "aliases", entry.Aliases);
        writer.WriteEndObject();
    }

    private static void WriteStringList(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
            writer.WriteStringValue(value);

        writer.WriteEndArray();
    }

    private static void WriteWarnings(Utf8JsonWriter writer, IReadOnlyList<string> warnings) =>
        WriteStringList(writer, "warnings", warnings);

    private static string LanguageName(int code) => code switch
    {
        1 => "Japanese",
        2 => "English",
        3 => "French",
        4 => "Italian",
        5 => "German",
        6 => "Spanish",
        7 => "Japanese (Kana)",
        8 => "Korean",
        _ => "unknown",
    };

    public static string SerializeManifest(FixtureManifest manifest, FixtureRecipe recipe)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", FixtureManifest.SchemaId);
            writer.WriteString("fixture_id", manifest.FixtureId);
            writer.WriteString("input_sha256", manifest.InputSha256);
            writer.WriteString("patch_sha256", manifest.PatchSha256);
            writer.WriteString("output_sha256", manifest.OutputSha256);
            writer.WriteString("pkhex_core_version", manifest.PkhexCoreVersion);
            writer.WriteString("pokeheartgold_commit", manifest.RegistryRevision.Contains('@') ? manifest.RegistryRevision.Split('@')[1] : manifest.RegistryRevision);
            writer.WriteString("symbol_registry", manifest.RegistryRevision);
            writer.WriteString("game", manifest.Game);
            writer.WriteString("structural_validation", manifest.StructuralValidation);
            writer.WriteString("semantic_validation", manifest.SemanticValidation);
            if (manifest.RuntimeResult is null)
                writer.WriteNull("runtime_result");
            else
                writer.WriteString("runtime_result", manifest.RuntimeResult);
            writer.WriteString("recipe_id", recipe.Id);
            writer.WriteString("base_path", recipe.BasePath);
            writer.WriteString("review_status", recipe.ReviewStatus);
            writer.WriteEndObject();
            writer.Flush();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static (FixtureManifest Manifest, string OutputFile) ParseManifest(byte[] bytes, string origin)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        string Get(string name)
        {
            if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
                throw HgssException.BadArguments("fixture-manifest", $"Manifest is missing '{name}' ({origin}).");
            return v.GetString()!;
        }

        var manifest = new FixtureManifest(
            root.TryGetProperty("schema", out var s) ? s.GetString() ?? "" : "",
            Get("fixture_id"), Get("input_sha256"), Get("patch_sha256"), Get("output_sha256"),
            Get("pkhex_core_version"), Get("symbol_registry"), Get("game"),
            Get("structural_validation"), Get("semantic_validation"),
            root.TryGetProperty("runtime_result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null);
        if (manifest.Schema != FixtureManifest.SchemaId)
            throw HgssException.BadArguments("fixture-manifest-schema", $"Manifest schema '{manifest.Schema}' is not supported (expected {FixtureManifest.SchemaId}).");
        return (manifest, manifest.FixtureId + ".sav");
    }

    /// <summary>Returns manifest bytes with only <c>runtime_result</c> replaced.</summary>
    public static byte[] SetManifestRuntime(byte[] bytes, string result)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("runtime_result"))
                    writer.WriteString("runtime_result", result);
                else
                    prop.WriteTo(writer);
            }

            if (!root.TryGetProperty("runtime_result", out _))
                writer.WriteString("runtime_result", result);
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.ToArray();
    }

    public static void WriteFixtureBuild(
        Utf8JsonWriter writer,
        FixtureRecipe recipe,
        string basePath,
        byte[] inputBytes,
        PatchResult result,
        string outputPath,
        string manifestPath,
        string libraryVersion)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "fixture.build");
        writer.WriteString("fixture_id", recipe.Id);
        writer.WriteString("game", recipe.Game);
        WriteInput(writer, basePath, inputBytes);
        writer.WriteString("output_path", outputPath);
        writer.WriteString("output_sha256", result.OutputSha256);
        writer.WriteString("manifest_path", manifestPath);
        writer.WriteString("review_status", recipe.ReviewStatus);
        WriteStringList(writer, "runtime_checkpoints", recipe.RuntimeCheckpoints);
        WriteValidation(writer, result.Validation);
        WriteByteAudit(writer, result.Audit, result.RequestedBytes, result.BookkeepingBytes);
        WriteWarnings(writer, ["semantic: not-evaluated; story validity requires runtime verification in melonDS."]);
        writer.WriteEndObject();
    }

    public static void WriteFixtureVerify(
        Utf8JsonWriter writer,
        string manifestPath,
        FixtureManifest manifest,
        string outputPath,
        StructuralValidation validation,
        string libraryVersion)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "fixture.verify");
        writer.WriteString("fixture_id", manifest.FixtureId);
        writer.WriteString("manifest", manifestPath);
        writer.WriteString("output", outputPath);
        writer.WriteString("output_sha256", manifest.OutputSha256);
        WriteValidation(writer, validation);
        writer.WriteString("pkhex_core_version", libraryVersion);
        WriteWarnings(writer, []);
        writer.WriteEndObject();
    }

    public static void WriteFixtureRecordRuntime(
        Utf8JsonWriter writer,
        string manifestPath,
        string fixtureId,
        string result,
        bool ok,
        string libraryVersion)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ReportSchema);
        writer.WriteString("operation", "fixture.record-runtime");
        writer.WriteString("fixture_id", fixtureId);
        writer.WriteString("manifest", manifestPath);
        writer.WriteString("runtime_result", result);
        writer.WriteBoolean("runtime_pass", ok);
        writer.WriteString("pkhex_core_version", libraryVersion);
        WriteWarnings(writer, []);
        writer.WriteEndObject();
    }
}
