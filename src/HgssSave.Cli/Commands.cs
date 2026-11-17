using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HgssSave.Core;
using HgssSave.PKHeX;

namespace HgssSave.Cli;

internal static class Commands
{
    private static readonly HgssFactory Factory = new();

    internal static int Version(string[] args)
    {
        var registry = TryLoadRegistry(args);
        Emit(w => ReportWriter.WriteVersion(w, Program.CliVersion, Factory.LibraryVersion, registry, Program.CommandNames));
        return 0;
    }

    internal static int Inspect(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save inspect INPUT [--symbols PATH] [--flags-limit N]");
        var registry = TryLoadRegistry(args);
        var limit = ParseInt(Args.Option(args, "--flags-limit", "200"), "--flags-limit");
        using var session = Factory.OpenWithRegistry(input, registry);
        var bytes = session.OriginalBytes;
        var snapshot = session.Snapshot();
        var validation = session.ValidateStructural();
        Emit(w => ReportWriter.WriteInspect(w, "inspect", input, bytes, snapshot, validation, registry, Factory.LibraryVersion, Warnings(registry), limit));
        return validation.Passed ? 0 : (int)ExitCode.StructuralFailure;
    }

    internal static int Validate(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save validate INPUT [--level structural]");
        var level = Args.Option(args, "--level", "structural");
        if (!string.Equals(level, "structural", StringComparison.OrdinalIgnoreCase))
            throw HgssException.Unsafe("validate-level", $"Validation level '{level}' is not implemented; only 'structural' is evaluated.");
        var registry = TryLoadRegistry(args);
        using var session = Factory.OpenWithRegistry(input, registry);
        var bytes = session.OriginalBytes;
        Emit(w => ReportWriter.WriteValidate(w, input, bytes, session.Snapshot(), session.ValidateStructural(), registry, Factory.LibraryVersion, Warnings(registry)));
        return session.ValidateStructural().Passed ? 0 : (int)ExitCode.StructuralFailure;
    }

    internal static int Diff(string[] args)
    {
        var before = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save diff BEFORE AFTER [--max-list N]");
        var after = Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "usage: hgss-save diff BEFORE AFTER [--max-list N]");
        var maxList = ParseInt(Args.Option(args, "--max-list", "200"), "--max-list");
        var maxRanges = ParseInt(Args.Option(args, "--max-ranges", "40"), "--max-ranges");
        var registry = TryLoadRegistry(args);
        using var sBefore = Factory.OpenWithRegistry(before, registry);
        using var sAfter = Factory.OpenWithRegistry(after, registry);
        var result = SaveDiff.Compare(sBefore, sAfter, registry, before, sBefore.OriginalBytes, after, sAfter.OriginalBytes, Factory.KnownRegions, maxList, maxRanges);
        Emit(w => ReportWriter.WriteDiff(w, result));
        return 0;
    }

    internal static int Patch(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save patch INPUT --recipe PATCH --out OUT [--dry-run]");
        var recipe = Args.Option(args, "--recipe") ?? throw HgssException.BadArguments("args", "patch requires --recipe PATCH.");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "patch requires --out OUT.");
        var document = PatchParser.Parse(ReadFile(recipe), recipe);
        var recipeDir = Path.GetDirectoryName(Path.GetFullPath(recipe));
        return RunPatch(input, document, ReadFile(recipe), output, args, recipeDir);
    }

    internal static int FlagGet(string[] args) => ScalarGet(args, "flag", "usage: hgss-save flag get INPUT ID");
    internal static int VarGet(string[] args) => ScalarGet(args, "variable", "usage: hgss-save var get INPUT ID");

    internal static int FlagSet(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save flag set INPUT ID true|false --out OUT");
        var id = Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "flag set requires an ID.");
        var value = ParseBool(Args.Positional(args, 2) ?? throw HgssException.BadArguments("args", "flag set requires a value."));
        var expect = ParseOptionalBool(Args.Option(args, "--expect"));
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "flag set requires --out OUT.");
        return RunPatch(input, new PatchDocument(PatchDocument.SchemaId, null, null, null, [new FlagSetOperation(id, value, expect)], []), CanonicalPatchJson($"flag.set:{id}:{value}:{expect}"), output, args);
    }

    internal static int VarSet(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save var set INPUT ID VALUE --out OUT");
        var id = Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "var set requires an ID.");
        var value = ParseInt(Args.Positional(args, 2) ?? throw HgssException.BadArguments("args", "var set requires a value."), "value");
        if ((uint)value > ushort.MaxValue)
            throw HgssException.BadArguments("var-range", $"Variable value {value} does not fit in 16 bits.");
        var expectRaw = Args.Option(args, "--expect");
        int? expect = expectRaw is null ? null : ParseInt(expectRaw, "--expect");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "var set requires --out OUT.");
        return RunPatch(input, new PatchDocument(PatchDocument.SchemaId, null, null, null, [new VariableSetOperation(id, value, expect)], []), CanonicalPatchJson($"variable.set:{id}:{value}:{expect}"), output, args);
    }

    internal static int BagSet(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save bag set INPUT ITEM QUANTITY --out OUT");
        var item = Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "bag set requires an item.");
        var quantity = ParseInt(Args.Positional(args, 2) ?? throw HgssException.BadArguments("args", "bag set requires a quantity."), "quantity");
        if (quantity < 0 || quantity > 999)
            throw HgssException.BadArguments("item-range", $"Quantity {quantity} is outside 0..999.");
        var expectRaw = Args.Option(args, "--expect");
        int? expect = expectRaw is null ? null : ParseInt(expectRaw, "--expect");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "bag set requires --out OUT.");
        return RunPatch(input, new PatchDocument(PatchDocument.SchemaId, null, null, null, [new BagQuantitySetOperation(item, quantity, expect)], []), CanonicalPatchJson($"inventory.quantity.set:{item}:{quantity}:{expect}"), output, args);
    }

    internal static int MoneySet(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save trainer money set INPUT VALUE --out OUT");
        var raw = Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "trainer money set requires a value.");
        if (!uint.TryParse(raw, out var value))
            throw HgssException.BadArguments("money-range", $"Money value '{raw}' is not a non-negative integer.");
        var expectRaw = Args.Option(args, "--expect");
        uint? expect = null;
        if (expectRaw is not null)
        {
            if (!uint.TryParse(expectRaw, out var parsed))
                throw HgssException.BadArguments("money-range", $"Expected money '{expectRaw}' is not a non-negative integer.");
            expect = parsed;
        }

        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "trainer money set requires --out OUT.");
        return RunPatch(input, new PatchDocument(PatchDocument.SchemaId, null, null, null, [new MoneySetOperation(value, expect)], []), CanonicalPatchJson($"trainer.money.set:{value}:{expect}"), output, args);
    }

    internal static int StoryCheck(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save story check INPUT [--badge-rules PATH] [--strict]");
        var registry = TryLoadRegistry(args);
        using var session = Factory.OpenWithRegistry(input, registry);
        var rules = BadgeRules.LoadDefault(Args.Option(args, "--badge-rules"));
        var violations = rules.Audit(session);
        Emit(w =>
        {
            w.WriteStartObject();
            w.WriteString("schema", "hgss-save.report/v1");
            w.WriteString("operation", "story.check");
            w.WriteString("input", input);
            w.WriteString("rules", rules.Path);
            w.WriteNumber("badges_johto", session.Trainer.JohtoBadges);
            w.WriteNumber("badges_kanto", session.Trainer.KantoBadges);
            w.WriteStartArray("locked_field_moves");
            foreach (var g in rules.LockedFieldMoves(session))
                w.WriteStringValue($"{g.Move} (needs {g.Name}; {g.Source})");
            w.WriteEndArray();
            w.WriteNumber("violation_count", violations.Count);
            w.WriteStartArray("violations");
            foreach (var v in violations)
            {
                w.WriteStartObject();
                w.WriteString("rule", v.Rule);
                w.WriteString("symbol", v.Symbol);
                w.WriteNumber("id", v.Id);
                w.WriteString("expected", v.Expected);
                w.WriteString("actual", v.Actual);
                w.WriteString("because", v.Because);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        });
        return violations.Count > 0 && Args.Flag(args, "--strict") ? (int)ExitCode.PreconditionFailed : 0;
    }

    internal static int StoryTriggers(string[] args)
    {
        var candidates = new List<string?>
        {
            Args.Option(args, "--scene-triggers"),
            Environment.GetEnvironmentVariable("HGSS_SCENE_TRIGGERS"),
            Path.Combine(AppContext.BaseDirectory, "hgss-us.scene-triggers.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "hgss-us.scene-triggers.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "registry", "hgss-us.scene-triggers.json"),
        };
        var path = candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c))
            ?? throw HgssException.BadArguments("triggers-missing", "Scene trigger table not found (looked at --scene-triggers, $HGSS_SCENE_TRIGGERS, beside the exe, ./, ./registry).");
        var varFilter = Args.Option(args, "--var");
        var mapFilter = Args.Option(args, "--map");
        System.Text.Json.Nodes.JsonNode? root;
        try
        {
            root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
        }
        catch (System.Text.Json.JsonException e)
        {
            throw HgssException.BadArguments("triggers-parse", $"Scene trigger table '{path}' is not valid JSON: {e.Message}");
        }

        if (root?["schema"]?.GetValue<string>() != "hgss-save.scene-triggers/v1" || root["triggers"] is not System.Text.Json.Nodes.JsonArray all)
            throw HgssException.BadArguments("triggers-schema", $"Scene trigger table '{path}' must declare schema 'hgss-save.scene-triggers/v1' with a 'triggers' array.");
        var selected = new System.Text.Json.Nodes.JsonArray();
        foreach (var t in all)
        {
            if (t is null)
                continue;
            var sym = t["var"]?.GetValue<string>();
            if (varFilter is not null && !string.Equals(sym, varFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (mapFilter is not null)
            {
                var name = t["map"]?.GetValue<string>();
                var id = t["map_id"]?.ToString();
                if (!string.Equals(name, mapFilter, StringComparison.OrdinalIgnoreCase) && id != mapFilter)
                    continue;
            }

            selected.Add(t.DeepClone());
        }

        var report = new System.Text.Json.Nodes.JsonObject
        {
            ["schema"] = "hgss-save.report/v1",
            ["operation"] = "story.triggers",
            ["table"] = path,
            ["count"] = selected.Count,
            ["triggers"] = selected,
        };
        Console.Out.WriteLine(report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static string FindPresetDir(string[] args)
    {
        var candidates = new List<string?>
        {
            Args.Option(args, "--presets"),
            Environment.GetEnvironmentVariable("HGSS_PRESETS"),
            Path.Combine(AppContext.BaseDirectory, "presets"),
            Path.Combine(Directory.GetCurrentDirectory(), "presets"),
            Path.Combine(Directory.GetCurrentDirectory(), "registry", "presets"),
        };
        foreach (var dir in candidates)
        {
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "index.json")))
                return dir;
        }

        throw HgssException.BadArguments("presets-missing", "No presets directory found (looked at --presets, $HGSS_PRESETS, beside the exe, ./presets, ./registry/presets).");
    }

    internal static int PresetList(string[] args)
    {
        var dir = FindPresetDir(args);
        Console.Out.WriteLine(File.ReadAllText(Path.Combine(dir, "index.json")));
        return 0;
    }

    internal static int PresetApply(string[] args)
    {
        var name = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save preset apply NAME INPUT --out OUT [--presets DIR]");
        var input = Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "preset apply requires INPUT.");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "preset apply requires --out OUT.");
        if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw HgssException.BadArguments("preset-name", $"Preset name '{name}' must be letters, digits and '-'.");
        var path = Path.Combine(FindPresetDir(args), name + ".json");
        if (name == "index" || !File.Exists(path))
            throw HgssException.BadArguments("preset-unknown", $"No preset named '{name}'. Run 'hgss-save preset list'.");
        var bytes = ReadFile(path);
        return RunPatch(input, PatchParser.Parse(bytes, path), bytes, output, args, Path.GetDirectoryName(path));
    }

    internal static int BadgesSet(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save trainer badges set INPUT [--johto MASK] [--kanto MASK] --out OUT");
        int? Mask(string name)
        {
            var raw = Args.Option(args, name);
            if (raw is null)
                return null;
            if (!int.TryParse(raw, out var v) || v is < 0 or > 255)
                throw HgssException.BadArguments("badges-range", $"{name} '{raw}' is not an integer 0..255.");
            return v;
        }

        var johto = Mask("--johto");
        var kanto = Mask("--kanto");
        if (johto is null && kanto is null)
            throw HgssException.BadArguments("args", "trainer badges set needs --johto and/or --kanto.");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "trainer badges set requires --out OUT.");
        return RunPatch(input, new PatchDocument(PatchDocument.SchemaId, null, null, null, [new BadgesSetOperation(johto, kanto) { Consequences = !Args.Flag(args, "--raw") }], []), CanonicalPatchJson($"trainer.badges.set:{johto}:{kanto}:{!Args.Flag(args, "--raw")}"), output, args);
    }

    internal static int FlagsList(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save flags list INPUT [--limit N]");
        var limit = ParseInt(Args.Option(args, "--limit", "5000"), "--limit");
        var registry = TryLoadRegistry(args);
        using var session = Factory.OpenWithRegistry(input, registry);
        Emit(w => ReportWriter.WriteFlags(w, input, session.OriginalBytes, session.Snapshot(), registry, session.SetFlagIds(), Factory.LibraryVersion, limit));
        return 0;
    }

    internal static int VarsList(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save vars list INPUT [--limit N]");
        var limit = ParseInt(Args.Option(args, "--limit", "500"), "--limit");
        var registry = TryLoadRegistry(args);
        using var session = Factory.OpenWithRegistry(input, registry);
        var nonzero = session.WorkValues().Where(p => p.Value != 0).ToDictionary(p => p.Key, p => p.Value);
        Emit(w => ReportWriter.WriteVars(w, input, session.OriginalBytes, session.Snapshot(), registry, nonzero, Factory.LibraryVersion, limit));
        return 0;
    }

    internal static int SymbolsLookup(string[] args)
    {
        var reference = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save symbols lookup NAME [--kind KIND]");
        var registry = RequireRegistry(args);
        var kindRaw = Args.Option(args, "--kind");
        var result = kindRaw is null ? registry.Lookup(reference) : registry.LookupAs(reference, ParseKind(kindRaw));
        Emit(w => ReportWriter.WriteSymbolsLookup(w, registry, reference, result, Factory.LibraryVersion));
        return result.Resolution == SymbolResolution.Resolved ? 0 : (int)ExitCode.SymbolFailure;
    }

    internal static int SymbolsSearch(string[] args)
    {
        var query = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save symbols search QUERY [--kind K1,K2] [--limit N]");
        if (string.IsNullOrWhiteSpace(query))
            throw HgssException.BadArguments("args", "Search query must not be empty.");
        var registry = RequireRegistry(args);
        var kinds = ParseKinds(Args.Option(args, "--kind") ?? Args.Option(args, "--kinds"));
        var limit = ParseInt(Args.Option(args, "--limit", "50"), "--limit");
        var results = registry.Search(query, kinds, limit);
        Emit(w => ReportWriter.WriteSymbolsSearch(w, registry, query, kinds ?? AllKinds(), results, Factory.LibraryVersion, limit));
        return 0;
    }

    internal static int ScriptIndex(string[] args)
    {
        var scripts = Args.Option(args, "--scripts") ?? throw HgssException.BadArguments("args", "script index requires --scripts DIR (pinned pokeheartgold checkout).");
        var commit = Args.Option(args, "--commit") ?? throw HgssException.BadArguments("args", "script index requires --commit SHA.");
        var output = Args.Option(args, "--out", "hgss-us.script-refs.json") ?? "hgss-us.script-refs.json";
        var project = Args.Option(args, "--project", "pokeheartgold") ?? "pokeheartgold";
        var scriptsDir = scripts.EndsWith("scr_seq", StringComparison.OrdinalIgnoreCase)
            ? scripts
            : Path.Combine(scripts, "files", "fielddata", "script", "scr_seq");
        var stats = ScriptIndexer.Build(scriptsDir, commit, project, output);
        Console.Out.WriteLine($"indexed {stats.Refs} refs to {stats.Symbols} symbols from {stats.Files} scripts at {commit} to {output}");
        return 0;
    }

    internal static int ScriptReferences(string[] args)
    {
        var symbol = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save script references SYMBOL [--script-refs PATH] [--limit N]");
        var limit = ParseInt(Args.Option(args, "--limit", "100"), "--limit");
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        var indexPath = Args.Option(args, "--script-refs") ?? Environment.GetEnvironmentVariable("HGSS_SCRIPT_REFS");
        if (indexPath is null)
        {
            var candidates = new List<string>();
            if (exeDir is not null)
                candidates.Add(Path.Combine(exeDir, "hgss-us.script-refs.json"));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "hgss-us.script-refs.json"));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "registry", "hgss-us.script-refs.json"));
            indexPath = candidates.FirstOrDefault(File.Exists) ?? candidates[^1];
        }

        if (!File.Exists(indexPath))
            throw HgssException.BadArguments("script-refs-missing", $"Script index not found: {indexPath ?? "(null)"}. Build one with 'script index'.");
        var refs = ScriptIndexer.Query(indexPath, symbol);
        Emit(w => ReportWriter.WriteScriptRefs(w, symbol, indexPath, refs, Factory.LibraryVersion, limit));
        return refs.Count == 0 ? (int)ExitCode.SymbolFailure : 0;
    }

    internal static int BuildRegistry(string[] args)
    {
        var headers = Args.Option(args, "--headers") ?? throw HgssException.BadArguments("args", "symbols build-registry requires --headers DIR (pinned pokeheartgold checkout).");
        var commit = Args.Option(args, "--commit") ?? throw HgssException.BadArguments("args", "symbols build-registry requires --commit SHA.");
        var output = Args.Option(args, "--out", "hgss-us.symbols.json") ?? "hgss-us.symbols.json";
        var registryId = Args.Option(args, "--registry-id", "hgss-us") ?? "hgss-us";
        var project = Args.Option(args, "--project", "pokeheartgold") ?? "pokeheartgold";
        var stats = RegistryBuilder.Build(headers, commit, registryId, project, output);
        Console.Out.WriteLine($"wrote {stats.Symbols} symbols ({stats.Skipped} skipped) from {stats.Files} headers at {commit} to {output}");
        Console.Out.WriteLine($"manifest: {stats.ManifestPath}");
        return 0;
    }

    internal static int FixtureBuild(string[] args)
    {
        var recipePath = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save fixture build RECIPE --bases DIR --outdir DIR");
        var basesDir = Args.Option(args, "--bases") ?? Directory.GetCurrentDirectory();
        var outDir = Args.Option(args, "--outdir") ?? throw HgssException.BadArguments("args", "fixture build requires --outdir DIR.");
        var force = Args.Flag(args, "--force");
        var maxRanges = ParseInt(Args.Option(args, "--max-ranges", "40"), "--max-ranges");
        var registry = TryLoadRegistry(args);
        var recipeBytes = ReadFile(recipePath);
        var recipe = FixtureParser.Parse(recipeBytes, recipePath);
        var recipeDir = Path.GetDirectoryName(Path.GetFullPath(recipePath)) ?? Directory.GetCurrentDirectory();
        var basePath = Path.IsPathRooted(recipe.BasePath) ? recipe.BasePath : Path.GetFullPath(Path.Combine(basesDir, recipe.BasePath));
        if (!File.Exists(basePath))
            throw HgssException.BadArguments("fixture-base-missing", $"Fixture base save not found: {basePath}. Point --bases at your local checkpoint directory; base saves are never committed.");
        var inputBytes = ReadFile(basePath);
        if (recipe.BaseSha256 is not null && !string.Equals(recipe.BaseSha256, HashUtil.Sha256(inputBytes), StringComparison.OrdinalIgnoreCase))
            throw HgssException.Precondition("fixture-base-sha", $"Fixture base sha256 does not match {basePath}.");
        using var session = Factory.OpenWithRegistry(basePath, registry);
        if (!string.Equals(session.Identity.Game, recipe.Game, StringComparison.OrdinalIgnoreCase))
            throw HgssException.Precondition("fixture-game", $"Fixture declares game '{recipe.Game}' but the base save identifies as '{session.Identity.Game}'.");
        var plan = PatchPlanner.Plan(session, recipe.Patch, registry, basePath, inputBytes, recipeBytes, false, recipeDir,
            Args.Option(args, "--safe-spawns") ?? Environment.GetEnvironmentVariable("HGSS_SAFE_SPAWNS"),
            Args.Option(args, "--warp-spawns"));
        if (!plan.CanApply)
        {
            Emit(w => ReportWriter.WritePatch(w, plan, null, null, false, Factory.LibraryVersion));
            return (int)ExitCode.PreconditionFailed;
        }

        var result = PatchRunner.Apply(session, plan, Factory, maxRanges);
        Directory.CreateDirectory(outDir);
        var outputPath = Path.Combine(outDir, recipe.Id + ".sav");
        var manifestPath = Path.Combine(outDir, recipe.Id + ".manifest.json");
        if ((File.Exists(outputPath) || File.Exists(manifestPath)) && !force)
            throw HgssException.OutputConflict("output-exists", $"Fixture output already exists in {outDir}. Pass --force to overwrite.");
        WriteOutputAtomic(outputPath, result.OutputBytes, true);
        var manifest = new FixtureManifest(
            FixtureManifest.SchemaId, recipe.Id, HashUtil.Sha256(inputBytes),
            HashUtil.Sha256(recipeBytes), result.OutputSha256, Factory.LibraryVersion,
            registry.Revision, session.Identity.Game,
            result.Validation.Passed ? "pass" : "fail", "needs-runtime-verification", null);
        WriteOutputAtomic(manifestPath, System.Text.Encoding.UTF8.GetBytes(ReportWriter.SerializeManifest(manifest, recipe)), true);
        Emit(w => ReportWriter.WriteFixtureBuild(w, recipe, basePath, inputBytes, result, outputPath, manifestPath, Factory.LibraryVersion));
        return 0;
    }

    internal static int FixtureVerify(string[] args)
    {
        var manifestPath = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save fixture verify MANIFEST --outdir DIR");
        var outDir = Args.Option(args, "--outdir") ?? Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
        var manifestBytes = ReadFile(manifestPath);
        var (manifest, outputFile) = ReportWriter.ParseManifest(manifestBytes, manifestPath);
        var outputPath = Path.Combine(outDir, outputFile);
        if (!File.Exists(outputPath))
            throw HgssException.BadArguments("fixture-output-missing", $"Fixture output not found: {outputPath}.");
        var outputBytes = ReadFile(outputPath);
        if (!string.Equals(HashUtil.Sha256(outputBytes), manifest.OutputSha256, StringComparison.OrdinalIgnoreCase))
            throw HgssException.Structural("fixture-hash", $"Output sha256 does not match manifest {manifestPath}.");
        using var session = Factory.OpenBytes(outputBytes, outputPath);
        var validation = session.ValidateStructural();
        Emit(w => ReportWriter.WriteFixtureVerify(w, manifestPath, manifest, outputPath, validation, Factory.LibraryVersion));
        return validation.Passed ? 0 : (int)ExitCode.StructuralFailure;
    }

    internal static int FixtureRecordRuntime(string[] args)
    {
        var manifestPath = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save fixture record-runtime MANIFEST --report REPORT.json");
        var reportPath = Args.Option(args, "--report") ?? throw HgssException.BadArguments("args", "fixture record-runtime requires --report REPORT.json (tools/leveld_run.py output).");
        var manifestBytes = ReadFile(manifestPath);
        var (manifest, _) = ReportWriter.ParseManifest(manifestBytes, manifestPath);
        var reportBytes = ReadFile(reportPath);
        var (saveSha, ok, summary) = ParseRuntimeReport(reportBytes, reportPath);
        if (!string.Equals(saveSha, manifest.OutputSha256, StringComparison.OrdinalIgnoreCase))
            throw HgssException.Precondition("runtime-sha",
                $"Runtime report {reportPath} covers save {saveSha} but manifest {manifestPath} expects {manifest.OutputSha256}. Refusing to attach a mismatched run.");
        var updated = ReportWriter.SetManifestRuntime(manifestBytes, summary);
        WriteOutputAtomic(manifestPath, updated, true);
        Emit(w => ReportWriter.WriteFixtureRecordRuntime(w, manifestPath, manifest.FixtureId, summary, ok, Factory.LibraryVersion));
        return ok ? 0 : (int)ExitCode.PreconditionFailed;
    }

    private static (string SaveSha, bool Ok, string Summary) ParseRuntimeReport(byte[] bytes, string origin)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (!root.TryGetProperty("save_sha256", out var sha) || sha.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(sha.GetString()))
                throw HgssException.BadArguments("runtime-report", $"Runtime report {origin} is missing 'save_sha256'.");
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var frames = root.TryGetProperty("frames_advanced", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : 0;
            var milestoneCount = root.TryGetProperty("milestones", out var m) && m.ValueKind == JsonValueKind.Array ? m.GetArrayLength() : 0;
            var label = root.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
            string? error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var summary = ok
                ? $"pass: headless melonDS-MCP boot of '{label}', {frames} frames advanced across {milestoneCount} milestones, report {origin}"
                : $"fail: headless run of '{label}' did not complete ({error ?? "unknown error"}), report {origin}";
            return (sha.GetString()!, ok, summary);
        }
        catch (JsonException e)
        {
            throw HgssException.BadArguments("runtime-report", $"Runtime report {origin} is not valid JSON: {e.Message}");
        }
    }

    internal static int ContainerProbe(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save container probe FILE");
        var kind = Factory.ProbeContainer(input);
        var bytes = File.ReadAllBytes(input);
        Emit(w =>
        {
            w.WriteStartObject();
            w.WriteString("schema", ReportWriter.ReportSchema);
            w.WriteString("operation", "container.probe");
            w.WriteStartObject("input");
            w.WriteString("path", input);
            w.WriteString("sha256", HashUtil.Sha256(bytes));
            w.WriteNumber("bytes", bytes.Length);
            w.WriteEndObject();
            w.WriteString("container", ContainerStrings.ToDisplay(kind));
            w.WriteEndObject();
        });
        return kind == ContainerKind.Other ? (int)ExitCode.UnsupportedSave : 0;
    }

    internal static int ContainerUnwrap(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save container unwrap FILE --out OUT");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "container unwrap requires --out OUT.");
        var force = Args.Flag(args, "--force");
        var registry = TryLoadRegistry(args);
        var probed = Factory.ProbeContainer(input);
        var bytes = Factory.UnwrapContainer(input);
        WriteOutputAtomic(output, bytes, force);
        Emit(w => ReportWriter.WriteUnwrap(w, input, probed, output, bytes, Factory.LibraryVersion, registry, []));
        return 0;
    }

    internal static int Blank(string[] args)
    {
        var game = Args.Option(args, "--game") ?? throw HgssException.BadArguments("args", "blank requires --game HG|SS.");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "blank requires --out OUT.");
        var force = Args.Flag(args, "--force");
        var kindRaw = Args.Option(args, "--kind") ?? "fresh-structure";
        var kind = kindRaw.ToLowerInvariant() switch
        {
            "fresh-structure" => BlankKind.FreshStructure,
            "erased-flash" => BlankKind.ErasedFlash,
            _ => throw HgssException.BadArguments("args", $"Unknown blank kind '{kindRaw}'."),
        };
        var options = new BlankOptions(
            game.ToUpperInvariant(),
            Args.Option(args, "--trainer-name"),
            ParseOptionalInt(Args.Option(args, "--tid"), "--tid"),
            ParseOptionalInt(Args.Option(args, "--sid"), "--sid"),
            ParseOptionalInt(Args.Option(args, "--gender"), "--gender"),
            ParseOptionalInt(Args.Option(args, "--language"), "--language"));
        var registry = TryLoadRegistry(args);
        var bytes = Factory.SynthesizeBlank(kind, options);
        WriteOutputAtomic(output, bytes, force);
        var warnings = kind == BlankKind.FreshStructure
            ? new[] { "Blank saves are structurally valid only; story state is not verified. Start from a real played save for fixtures." }
            : new[] { "Erased-flash image has no save structure and cannot be opened; for negative tests only." };
        Emit(w => ReportWriter.WriteBlank(w, output, bytes, kind, options, Factory.LibraryVersion, registry, warnings));
        return 0;
    }

    internal static int PartyExport(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save party export INPUT SLOT --out FILE.pk4");
        var slotRaw = Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "party export requires a slot 0..5.");
        var slot = ParseInt(slotRaw, "slot");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "party export requires --out FILE.pk4.");
        var registry = TryLoadRegistry(args);
        using var session = Factory.OpenWithRegistry(input, registry);
        var bytes = session.ExportPartySlot(slot);
        var preview = session.PreviewPkFile(bytes);
        WriteOutputAtomic(output, bytes, Args.Flag(args, "--force"));
        Emit(w => ReportWriter.WriteValue(w, "party.export", input, session.OriginalBytes, $"party.{slot}.species", preview.Species.ToString(), Factory.LibraryVersion, registry, Warnings(registry)));
        return 0;
    }

    internal static int PartyImport(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save party import INPUT SLOT FILE.pk4 --out OUT [--expect-species N] [--no-dex]");
        var slot = ParseInt(Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "party import requires a slot 0..5."), "slot");
        var file = Args.Positional(args, 2) ?? throw HgssException.BadArguments("args", "party import requires a .pk4 file.");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "party import requires --out OUT.");
        var expectRaw = Args.Option(args, "--expect-species");
        int? expect = expectRaw is null ? null : ParseInt(expectRaw, "--expect-species");
        var updateDex = !Args.Flag(args, "--no-dex");
        var recipeDir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "";
        var doc = new PatchDocument(PatchDocument.SchemaId, null, null, null, [new PartyImportOperation(slot, file, expect, updateDex)], []);
        return RunPatch(input, doc, CanonicalPatchJson($"party.slot.import:{slot}:{file}:{expect}:{updateDex}"), output, args, recipeDir);
    }

    internal static int PartyClear(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save party clear INPUT SLOT --out OUT");
        var slot = ParseInt(Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "party clear requires a slot 0..5."), "slot");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "party clear requires --out OUT.");
        var doc = new PatchDocument(PatchDocument.SchemaId, null, null, null, [new PartyClearOperation(slot)], []);
        return RunPatch(input, doc, CanonicalPatchJson($"party.slot.clear:{slot}"), output, args);
    }

    internal static int BoxExport(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save box export INPUT BOX SLOT --out FILE.pk4");
        var box = ParseInt(Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "box export requires a box 0..17."), "box");
        var slot = ParseInt(Args.Positional(args, 2) ?? throw HgssException.BadArguments("args", "box export requires a slot 0..29."), "slot");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "box export requires --out FILE.pk4.");
        var registry = TryLoadRegistry(args);
        using var session = Factory.OpenWithRegistry(input, registry);
        var bytes = session.ExportBoxSlot(box, slot);
        var preview = session.PreviewPkFile(bytes);
        WriteOutputAtomic(output, bytes, Args.Flag(args, "--force"));
        Emit(w => ReportWriter.WriteValue(w, "box.export", input, session.OriginalBytes, $"box.{box}.{slot}.species", preview.Species.ToString(), Factory.LibraryVersion, registry, Warnings(registry)));
        return 0;
    }

    internal static int BoxImport(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save box import INPUT BOX SLOT FILE.pk4 --out OUT [--expect-species N] [--no-dex]");
        var box = ParseInt(Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "box import requires a box 0..17."), "box");
        var slot = ParseInt(Args.Positional(args, 2) ?? throw HgssException.BadArguments("args", "box import requires a slot 0..29."), "slot");
        var file = Args.Positional(args, 3) ?? throw HgssException.BadArguments("args", "box import requires a .pk4 file.");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "box import requires --out OUT.");
        var expectRaw = Args.Option(args, "--expect-species");
        int? expect = expectRaw is null ? null : ParseInt(expectRaw, "--expect-species");
        var updateDex = !Args.Flag(args, "--no-dex");
        var recipeDir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "";
        var doc = new PatchDocument(PatchDocument.SchemaId, null, null, null, [new BoxImportOperation(box, slot, file, expect, updateDex)], []);
        return RunPatch(input, doc, CanonicalPatchJson($"box.slot.import:{box}:{slot}:{file}:{expect}:{updateDex}"), output, args, recipeDir);
    }

    internal static int BoxClear(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save box clear INPUT BOX SLOT --out OUT");
        var box = ParseInt(Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "box clear requires a box 0..17."), "box");
        var slot = ParseInt(Args.Positional(args, 2) ?? throw HgssException.BadArguments("args", "box clear requires a slot 0..29."), "slot");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "box clear requires --out OUT.");
        var doc = new PatchDocument(PatchDocument.SchemaId, null, null, null, [new BoxClearOperation(box, slot)], []);
        return RunPatch(input, doc, CanonicalPatchJson($"box.slot.clear:{box}:{slot}"), output, args);
    }

    internal static int LocationSet(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save location set INPUT MAP X Y Z --out OUT [--safe-spawns PATH]");
        var map = ParseInt(Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "location set requires a map id."), "map");
        var x = ParseInt(Args.Positional(args, 2) ?? throw HgssException.BadArguments("args", "location set requires X."), "x");
        var y = ParseInt(Args.Positional(args, 3) ?? throw HgssException.BadArguments("args", "location set requires Y."), "y");
        var z = ParseInt(Args.Positional(args, 4) ?? throw HgssException.BadArguments("args", "location set requires Z."), "z");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "location set requires --out OUT.");
        var doc = new PatchDocument(PatchDocument.SchemaId, null, null, null, [new LocationSetOperation(map, x, y, z)], []);
        return RunPatch(input, doc, CanonicalPatchJson($"location.set:{map}:{x}:{y}:{z}"), output, args);
    }

    internal static int LocationWarp(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save location warp INPUT MAP WARP --out OUT [--direction N] [--warp-spawns PATH]");
        var map = ParseInt(Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "location warp requires a map id."), "map");
        var warp = ParseInt(Args.Positional(args, 2) ?? throw HgssException.BadArguments("args", "location warp requires a warp index."), "warp");
        var direction = ParseInt(Args.Option(args, "--direction", "1"), "--direction");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "location warp requires --out OUT.");
        var doc = new PatchDocument(PatchDocument.SchemaId, null, null, null, [new LocationWarpOperation(map, warp, direction)], []);
        return RunPatch(input, doc, CanonicalPatchJson($"location.warp:{map}:{warp}:{direction}"), output, args);
    }

    internal static int DexSet(string[] args)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", "usage: hgss-save dex set INPUT SPECIES --seen BOOL --caught BOOL --out OUT");
        var species = ParseInt(Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", "dex set requires a species id 1..493."), "species");
        var seenRaw = Args.Option(args, "--seen") ?? throw HgssException.BadArguments("args", "dex set requires --seen true|false.");
        var caughtRaw = Args.Option(args, "--caught") ?? throw HgssException.BadArguments("args", "dex set requires --caught true|false.");
        var output = Args.Option(args, "--out") ?? throw HgssException.BadArguments("args", "dex set requires --out OUT.");
        var doc = new PatchDocument(PatchDocument.SchemaId, null, null, null, [new DexSetOperation(species, ParseBool(seenRaw), ParseBool(caughtRaw))], []);
        return RunPatch(input, doc, CanonicalPatchJson($"dex.set:{species}:{seenRaw}:{caughtRaw}"), output, args);
    }

    private static int ScalarGet(string[] args, string field, string usage)
    {
        var input = Args.Positional(args, 0) ?? throw HgssException.BadArguments("args", usage);
        var reference = Args.Positional(args, 1) ?? throw HgssException.BadArguments("args", usage);
        var registry = TryLoadRegistry(args);
        using var session = Factory.OpenWithRegistry(input, registry);
        var (label, value) = field switch
        {
            "flag" => ResolveFlag(session, registry, reference),
            _ => ResolveVar(session, registry, reference),
        };
        Emit(w => ReportWriter.WriteValue(w, $"{field}.get", input, session.OriginalBytes, label, value, Factory.LibraryVersion, registry, Warnings(registry)));
        return 0;
    }

    private static (string Label, string Value) ResolveFlag(ISaveSession session, SymbolRegistry registry, string reference)
    {
        var lookup = registry.LookupAs(reference, SymbolKind.PersistentFlag);
        int id = lookup.Resolution switch
        {
            SymbolResolution.Resolved => lookup.Entry!.Id,
            SymbolResolution.NumericUnreviewed when SymbolRegistry.TryParseNumber(reference, out var n) => n,
            _ => throw HgssException.Symbol("flag-unknown", $"Flag '{reference}' did not resolve to a persistent save flag in {registry.Revision}."),
        };
        if (!session.TryGetFlag(id, out var value))
            throw HgssException.Precondition("flag-range", $"Flag id {id} is outside the persistent range.");
        var name = lookup.Entry?.Name ?? registry.NameFor(SymbolKind.PersistentFlag, id);
        return ($"flag.{name ?? id.ToString()}", value ? "true" : "false");
    }

    private static (string Label, string Value) ResolveVar(ISaveSession session, SymbolRegistry registry, string reference)
    {
        var lookup = registry.LookupAs(reference, SymbolKind.SaveWork);
        int id = lookup.Resolution switch
        {
            SymbolResolution.Resolved => lookup.Entry!.Id,
            SymbolResolution.NumericUnreviewed when SymbolRegistry.TryParseNumber(reference, out var n) => n,
            _ => throw HgssException.Symbol("variable-unknown", $"Variable '{reference}' did not resolve to a save-work slot in {registry.Revision}."),
        };
        if (!session.TryGetVar(id, out var value))
            throw HgssException.Precondition("variable-range", $"Variable id {id} is not a save-work slot.");
        var name = lookup.Entry?.Name ?? registry.NameFor(SymbolKind.SaveWork, id);
        return ($"variable.{name ?? $"0x{id:X4}"}", value.ToString());
    }

    private static int RunPatch(string input, PatchDocument document, byte[] patchBytes, string output, string[] args, string? recipeDirectory = null)
    {
        var dryRun = Args.Flag(args, "--dry-run");
        var requireSymbols = Args.Flag(args, "--require-symbols");
        var maxRanges = ParseInt(Args.Option(args, "--max-ranges", "40"), "--max-ranges");
        var safeSpawns = Args.Option(args, "--safe-spawns") ?? Environment.GetEnvironmentVariable("HGSS_SAFE_SPAWNS");
        var registry = TryLoadRegistry(args);
        var inputBytes = ReadFile(input);
        using var session = Factory.OpenWithRegistry(input, registry);
        var warpSpawns = Args.Option(args, "--warp-spawns");
        var plan = PatchPlanner.Plan(session, document, registry, input, inputBytes, patchBytes, requireSymbols, recipeDirectory, safeSpawns, warpSpawns, Args.Option(args, "--badge-rules"));

        if (!plan.CanApply)
        {
            Emit(w => ReportWriter.WritePatch(w, plan, null, null, dryRun, Factory.LibraryVersion));
            return (int)ExitCode.PreconditionFailed;
        }

        if (dryRun)
        {
            Emit(w => ReportWriter.WritePatch(w, plan, null, null, true, Factory.LibraryVersion));
            return 0;
        }

        var result = PatchRunner.Apply(session, plan, Factory, maxRanges);
        WriteOutputAtomic(output, result.OutputBytes, Args.Flag(args, "--force"));
        Emit(w => ReportWriter.WritePatch(w, plan, result, output, false, Factory.LibraryVersion));
        return 0;
    }

    private static void WriteOutputAtomic(string output, byte[] bytes, bool force)
    {
        if (File.Exists(output) && !force)
            throw HgssException.OutputConflict("output-exists", $"Output '{output}' already exists. Pass --force to overwrite.");
        var dir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir ?? ".", ".hgss-save-" + Path.GetRandomFileName());
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, output, force);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static SymbolRegistry TryLoadRegistry(string[] args)
    {
        var explicitPath = Args.Option(args, "--symbols") ?? Environment.GetEnvironmentVariable("HGSS_SYMBOLS");
        if (explicitPath is not null)
            return SymbolRegistry.Load(explicitPath);

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        var candidates = new List<string?>();
        if (exeDir is not null)
            candidates.Add(Path.Combine(exeDir, "hgss-us.symbols.json"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "hgss-us.symbols.json"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "registry", "hgss-us.symbols.json"));
        foreach (var candidate in candidates)
        {
            if (candidate is not null && File.Exists(candidate))
                return SymbolRegistry.Load(candidate);
        }

        return SymbolRegistry.Empty;
    }

    private static SymbolRegistry RequireRegistry(string[] args)
    {
        var explicitPath = Args.Option(args, "--symbols") ?? Environment.GetEnvironmentVariable("HGSS_SYMBOLS");
        if (explicitPath is not null)
            return SymbolRegistry.Load(explicitPath);

        var registry = TryLoadRegistry(args);
        if (!ReferenceEquals(registry, SymbolRegistry.Empty))
            return registry;
        throw HgssException.Symbol("registry-missing",
            "Symbol registry not found. Pass --symbols PATH or set HGSS_SYMBOLS; build one with 'symbols build-registry'.");
    }

    private static IReadOnlyList<string> Warnings(SymbolRegistry registry) =>
        ReferenceEquals(registry, SymbolRegistry.Empty)
            ? ["symbol registry not loaded; named flag/variable/item lookup is unavailable"]
            : [];

    private static byte[] ReadFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw HgssException.OutputConflict("input-io", $"Cannot read '{path}': {e.Message}");
        }
    }

    private static byte[] CanonicalPatchJson(string tag) =>
        System.Text.Encoding.UTF8.GetBytes($"{{\"schema\":\"{PatchDocument.SchemaId}\",\"cli\":\"{Program.CliVersion}\",\"op\":\"{tag}\"}}");

    private static int ParseInt(string? raw, string name)
    {
        if (raw is null || !int.TryParse(raw, out var value))
            throw HgssException.BadArguments("args", $"Option '{name}' needs an integer (got '{raw ?? ""}').");
        return value;
    }

    private static int? ParseOptionalInt(string? raw, string name) =>
        raw is null ? null : ParseInt(raw, name);

    private static bool ParseBool(string raw) => raw.ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw HgssException.BadArguments("args", $"Expected true/false (got '{raw}')."),
    };

    private static bool? ParseOptionalBool(string? raw) =>
        raw is null ? null : ParseBool(raw);

    private static SymbolKind ParseKind(string raw) => raw.ToLowerInvariant() switch
    {
        "flag" or "persistent_flag" => SymbolKind.PersistentFlag,
        "var" or "variable" or "save_work" => SymbolKind.SaveWork,
        "map" => SymbolKind.Map,
        "item" => SymbolKind.Item,
        "species" => SymbolKind.Species,
        "move" => SymbolKind.Move,
        _ => throw HgssException.BadArguments("args", $"Unknown symbol kind '{raw}'."),
    };

    private static IReadOnlyList<SymbolKind>? ParseKinds(string? raw)
    {
        if (raw is null)
            return null;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ParseKind).ToList();
    }

    private static IReadOnlyList<SymbolKind> AllKinds() =>
        [SymbolKind.PersistentFlag, SymbolKind.SaveWork, SymbolKind.Map, SymbolKind.Item, SymbolKind.Species, SymbolKind.Move];

    private static void Emit(Action<Utf8JsonWriter> write)
    {
        Console.Out.WriteLine(ReportWriter.Serialize(write, indent: true));
    }
}
