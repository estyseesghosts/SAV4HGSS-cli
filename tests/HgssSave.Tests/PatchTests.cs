using System;
using System.Linq;
using System.Text;
using HgssSave.Core;
using HgssSave.PKHeX;
using Xunit;

namespace HgssSave.Tests;

public sealed class PatchTests
{
    private static readonly HgssFactory Factory = new();

    private const string TestRegistryJson = """
        {"schema":"hgss-save.symbols/v1","registry_id":"test","source":{"project":"test","commit":"c","files":[]},
        "symbols":[
        {"name":"FLAG_CAUGHT_LUGIA","kind":"persistent_flag","id":279,"hex":"0x117","source":"t","line":1,"writable":true,"aliases":[],"confidence":"t"},
        {"name":"FLAG_MAPTEMP_001","kind":"temporary_flag","id":1,"hex":"0x1","source":"t","line":2,"writable":false,"aliases":[],"confidence":"t"},
        {"name":"VAR_PLAYER_STARTER","kind":"save_work","id":16432,"hex":"0x4030","source":"t","line":3,"writable":true,"aliases":[],"confidence":"t"},
        {"name":"VAR_TEMP_x4000","kind":"temporary_var","id":16384,"hex":"0x4000","source":"t","line":4,"writable":false,"aliases":[],"confidence":"t"},
        {"name":"VAR_SPECIAL_x8000","kind":"special_runtime_var","id":32768,"hex":"0x8000","source":"t","line":6,"writable":false,"aliases":[],"confidence":"t"},
        {"name":"ITEM_POTION","kind":"item","id":17,"hex":"0x11","source":"t","line":5,"writable":false,"aliases":[],"confidence":"t"}],
        "skipped":[]}
        """;

    private static SymbolRegistry TestRegistry() =>
        SymbolRegistry.Parse(Encoding.UTF8.GetBytes(TestRegistryJson), "test");

    private static ISaveSession OpenBlank() =>
        Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");

    private static PatchDocument Parse(string json) =>
        PatchParser.Parse(Encoding.UTF8.GetBytes(json), "test");

    [Fact]
    public void NamedPatchAppliesAtomically()
    {
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","game":"HG","operations":[
            {"op":"flag.set","id":"FLAG_CAUGHT_LUGIA","value":true},
            {"op":"variable.set","id":"VAR_PLAYER_STARTER","value":2},
            {"op":"trainer.money.set","value":50000},
            {"op":"inventory.quantity.set","item":"ITEM_POTION","quantity":5}],
            "assert":[{"field":"trainer.money","equals":50000}]}
            """);
        var patchBytes = Encoding.UTF8.GetBytes("{}");
        var plan = PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, patchBytes, false);
        Assert.True(plan.CanApply);
        Assert.Empty(plan.PreconditionFailures);
        Assert.Empty(plan.Conflicts);
        var result = PatchRunner.Apply(session, plan, Factory);
        Assert.True(result.Audit.Accepted);
        Assert.Equal(0, result.Audit.UnclassifiedBytes);
        Assert.Empty(result.PostconditionFailures);
        Assert.True(result.Validation.Passed);
        Assert.True(session.TryGetFlag(279, out var flag) && flag);
    }

    [Fact]
    public void HexIdsResolve()
    {
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[
            {"op":"flag.set","id":"0x117","value":true},
            {"op":"variable.set","id":"0x4030","value":1}]}
            """);
        var plan = PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false);
        Assert.True(plan.CanApply);
    }

    [Fact]
    public void ExpectMismatchFailsWithoutWriting()
    {
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[
            {"op":"trainer.money.set","value":500,"expect":999}]}
            """);
        var plan = PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false);
        Assert.False(plan.CanApply);
        Assert.NotEmpty(plan.PreconditionFailures);
    }

    [Fact]
    public void ConflictingWritesAreRejected()
    {
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[
            {"op":"trainer.money.set","value":100},
            {"op":"trainer.money.set","value":200}]}
            """);
        var plan = PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false);
        Assert.False(plan.CanApply);
        Assert.NotEmpty(plan.Conflicts);
    }

    [Fact]
    public void DuplicateIdenticalWritesAreAllowedOnce()
    {
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[
            {"op":"flag.set","id":279,"value":true},
            {"op":"flag.set","id":279,"value":true}]}
            """);
        var plan = PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false);
        Assert.True(plan.CanApply);
        Assert.Single(plan.Operations);
    }

    [Fact]
    public void TemporaryFlagWriteIsUnsafe()
    {
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[{"op":"flag.set","id":"FLAG_MAPTEMP_001","value":true}]}
            """);
        var error = Assert.Throws<HgssException>(() =>
            PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false));
        Assert.Equal(ExitCode.UnsafeOperation, error.ExitCode);
    }

    [Fact]
    public void TemporaryVarWriteAppliesWithAMapLoadWarning()
    {
        // Temp vars are stored in SaveVarsFlags.vars[] (vars.h TEMP_VAR_BASE 0x4000, NUM_VARS 0x170) and
        // zeroed by ScriptManager on map load (script_manager.c:406-407). The named form must behave like
        // the numeric spelling: apply, warn, and say why the value is short-lived.
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[{"op":"variable.set","id":"VAR_TEMP_x4000","value":1}]}
            """);
        var plan = PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false);
        Assert.True(plan.CanApply);
        Assert.Contains(plan.Warnings, w => w.Contains("VAR_TEMP_x4000") && w.Contains("map load"));
        var result = PatchRunner.Apply(session, plan, Factory);
        Assert.True(result.Audit.Accepted);
        Assert.True(session.TryGetVar(0x4000, out var value) && value == 1);
    }

    [Fact]
    public void SpecialVarWriteIsUnsafe()
    {
        // VAR_SPECIAL_* lives at 0x8000, outside the saved window 0x4000..0x416F (vars.h VARS_END).
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[{"op":"variable.set","id":"VAR_SPECIAL_x8000","value":1}]}
            """);
        var error = Assert.Throws<HgssException>(() =>
            PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false));
        Assert.Equal(ExitCode.UnsafeOperation, error.ExitCode);
    }

    [Fact]
    public void UnknownNameIsSymbolFailure()
    {
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[{"op":"flag.set","id":"FLAG_NOPE","value":true}]}
            """);
        var error = Assert.Throws<HgssException>(() =>
            PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false));
        Assert.Equal(ExitCode.SymbolFailure, error.ExitCode);
    }

    [Fact]
    public void NumericIdsWorkWithoutRegistry()
    {
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[
            {"op":"flag.set","id":279,"value":true},
            {"op":"variable.set","id":16432,"value":7},
            {"op":"inventory.quantity.set","item":17,"quantity":2}]}
            """);
        var plan = PatchPlanner.Plan(session, document, SymbolRegistry.Empty, "blank", session.OriginalBytes, [1], false);
        Assert.True(plan.CanApply);
        Assert.Equal(3, plan.Warnings.Count(w => w.Contains("numeric/unreviewed")));
        var result = PatchRunner.Apply(session, plan, Factory);
        Assert.True(result.Audit.Accepted);
    }

    [Fact]
    public void RequireSymbolsRejectsNumericIds()
    {
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[{"op":"flag.set","id":279,"value":true}]}
            """);
        var error = Assert.Throws<HgssException>(() =>
            PatchPlanner.Plan(session, document, SymbolRegistry.Empty, "blank", session.OriginalBytes, [1], true));
        Assert.Equal(ExitCode.SymbolFailure, error.ExitCode);
    }

    [Fact]
    public void BaseShaMismatchFailsClosed()
    {
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","base_sha256":"0000000000000000000000000000000000000000000000000000000000000000",
            "operations":[{"op":"trainer.money.set","value":1}]}
            """);
        var plan = PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false);
        Assert.False(plan.CanApply);
    }

    [Fact]
    public void GameMismatchFailsClosed()
    {
        var registry = TestRegistry();
        using var session = OpenBlank();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","game":"SS","operations":[{"op":"trainer.money.set","value":1}]}
            """);
        var plan = PatchPlanner.Plan(session, document, registry, "blank", session.OriginalBytes, [1], false);
        Assert.False(plan.CanApply);
    }

    [Fact]
    public void ParserRejectsUnknownOperation()
    {
        var error = Assert.Throws<HgssException>(() => Parse("""
            {"schema":"hgss-save.patch/v1","operations":[{"op":"party.wipe","value":true}]}
            """));
        Assert.Equal(ExitCode.BadArguments, error.ExitCode);
    }

    [Fact]
    public void ParserRejectsUnknownKeys()
    {
        var error = Assert.Throws<HgssException>(() => Parse("""
            {"schema":"hgss-save.patch/v1","operations":[],"bogus":1}
            """));
        Assert.Equal(ExitCode.BadArguments, error.ExitCode);
    }

    [Fact]
    public void ParserRejectsWrongSchema()
    {
        var error = Assert.Throws<HgssException>(() => Parse("""
            {"schema":"hgss-save.patch/v9","operations":[]}
            """));
        Assert.Equal(ExitCode.BadArguments, error.ExitCode);
    }

    [Fact]
    public void DiffFindsFlagAndMoneyChanges()
    {
        var registry = TestRegistry();
        using var before = OpenBlank();
        var beforeBytes = (byte[])before.OriginalBytes.Clone();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[
            {"op":"flag.set","id":279,"value":true},
            {"op":"trainer.money.set","value":123}]}
            """);
        var plan = PatchPlanner.Plan(before, document, registry, "before", beforeBytes, [1], false);
        var result = PatchRunner.Apply(before, plan, Factory);
        using var beforeView = Factory.OpenBytes(beforeBytes, "before");
        using var after = Factory.OpenBytes(result.OutputBytes, "after");
        var diff = SaveDiff.Compare(beforeView, after, registry, "before", beforeBytes, "after", result.OutputBytes, Factory.KnownRegions);
        Assert.Single(diff.Flags, f => f.Id == 279 && f.Direction == "set");
        Assert.Contains(diff.Fields, f => f.Field == "trainer.money" && f.After == "123");
        Assert.False(diff.SameFile);
    }

    [Fact]
    public void DiffNamesTemporaryVars()
    {
        // Temp vars share SaveVarsFlags.vars[] with save-work vars (F23), so a diff must name them the
        // same way it names 0x4030. Asking the registry for SaveWork alone printed "name": null here.
        var registry = TestRegistry();
        using var before = OpenBlank();
        var beforeBytes = (byte[])before.OriginalBytes.Clone();
        var document = Parse("""
            {"schema":"hgss-save.patch/v1","operations":[{"op":"variable.set","id":"VAR_TEMP_x4000","value":7}]}
            """);
        var plan = PatchPlanner.Plan(before, document, registry, "before", beforeBytes, [1], false);
        var result = PatchRunner.Apply(before, plan, Factory);
        using var beforeView = Factory.OpenBytes(beforeBytes, "before");
        using var after = Factory.OpenBytes(result.OutputBytes, "after");
        var diff = SaveDiff.Compare(beforeView, after, registry, "before", beforeBytes, "after", result.OutputBytes, Factory.KnownRegions);
        Assert.Contains(diff.Vars, v => v.Id == 0x4000 && v.Name == "VAR_TEMP_x4000" && v.After == 7);
    }
}
