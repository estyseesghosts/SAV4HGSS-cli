using System;
using System.Text;
using HgssSave.Core;
using HgssSave.PKHeX;
using PKHeX.Core;
using Xunit;

namespace HgssSave.Tests;

public sealed class PartyBoxTests
{
    private static readonly HgssFactory Factory = new();

    private static byte[] BuildPkBytes(int species)
    {
        var pk = new PK4();
        pk.Species = (ushort)species;
        pk.CurrentLevel = 5;
        pk.EXP = 100;
        pk.TID16 = 1;
        pk.SID16 = 2;
        pk.RefreshChecksum();
        Assert.True(pk.ChecksumValid);
        return pk.Data.ToArray();
    }

    [Fact]
    public void PartyImportRoundTripSurvivesExport()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        var pk = BuildPkBytes(25);
        var preview = session.PreviewPkFile(pk);
        Assert.Equal(25, preview.Species);
        Assert.True(preview.ChecksumValid);
        session.ImportPartySlot(0, pk, updateDex: true);
        Assert.Equal(25, session.PartySpecies(0));
        var export = session.Export();
        Assert.True(export.ChecksumsValid);
        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        Assert.Equal(25, reparsed.PartySpecies(0));
        Assert.True(reparsed.Snapshot().Party[0].Present);
    }

    [Fact]
    public void PartyClearRemovesSlot()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        session.ImportPartySlot(0, BuildPkBytes(25), updateDex: false);
        session.ClearPartySlot(0);
        Assert.Equal(0, session.PartySpecies(0));
    }

    [Fact]
    public void BoxImportRoundTripSurvivesExport()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        session.ImportBoxSlot(0, 0, BuildPkBytes(155), updateDex: false);
        Assert.Equal(155, session.BoxSpecies(0, 0));
        var exported = session.ExportBoxSlot(0, 0);
        Assert.Equal(136, exported.Length);
        var export = session.Export();
        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        Assert.Equal(155, reparsed.BoxSpecies(0, 0));
    }

    [Fact]
    public void InvalidPkSizeIsRejected()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        Assert.Throws<HgssException>(() => session.ImportPartySlot(0, new byte[10], updateDex: false));
    }

    [Fact]
    public void CorruptPkChecksumIsRejected()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        var pk = BuildPkBytes(25);
        pk[6] ^= 0xFF;
        pk[7] ^= 0xFF;
        var error = Assert.Throws<HgssException>(() => session.ImportPartySlot(0, pk, updateDex: false));
        Assert.Equal(ExitCode.StructuralFailure, error.ExitCode);
    }

    [Fact]
    public void OutOfRangeSlotsAreRejected()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        var pk = BuildPkBytes(25);
        Assert.Throws<HgssException>(() => session.ImportPartySlot(6, pk, updateDex: false));
        Assert.Throws<HgssException>(() => session.ImportBoxSlot(18, 0, pk, updateDex: false));
        Assert.Throws<HgssException>(() => session.ImportBoxSlot(0, 30, pk, updateDex: false));
    }

    [Fact]
    public void LocationRoundTripMirrorsCoordinates()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        session.SetLocation(73, 459, 246, 3);
        Assert.Equal(73, session.Location.MapId);
        Assert.Equal(459, session.Location.X);
        Assert.Equal(459, session.Location.SecondaryX);
        Assert.Equal(246, session.Location.Y);
        Assert.Equal(246, session.Location.SecondaryY);
        Assert.Equal(3, session.Location.Z);
        var export = session.Export();
        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        Assert.Equal(73, reparsed.Location.MapId);
        Assert.Equal(459, reparsed.Location.X);
    }

    [Fact]
    public void DexSetRoundTripSurvivesExport()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        session.SetDex(25, seen: true, caught: true);
        var (seen, caught) = session.GetDexStatus(25);
        Assert.True(seen);
        Assert.True(caught);
        var export = session.Export();
        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        Assert.True(reparsed.GetDexStatus(25).Caught);
    }

    [Fact]
    public void DexCaughtWithoutSeenIsRejected()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        Assert.Throws<HgssException>(() => session.SetDex(25, seen: false, caught: true));
        var error = Assert.Throws<HgssException>(() => PatchParser.Parse(Encoding.UTF8.GetBytes("""
            {"schema":"hgss-save.patch/v1","operations":[{"op":"dex.set","species":25,"seen":false,"caught":true}]}
            """), "test"));
        Assert.Equal(ExitCode.BadArguments, error.ExitCode);
    }

    private static string SafeSpawnsPath()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "fixtures", "safe-spawns.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new System.IO.FileNotFoundException("fixtures/safe-spawns.json not found above the test directory.");
    }

    [Fact]
    public void SafeSpawnGateAcceptsListedSpawns()
    {
        var spawns = SafeSpawnRegistry.Load(SafeSpawnsPath());
        Assert.True(SafeSpawnRegistry.Contains(spawns, 73, 459, 246, 3));
        Assert.False(SafeSpawnRegistry.Contains(spawns, 73, 1, 1, 3));
    }

    [Fact]
    public void LocationOutsideRegistryIsUnsafe()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        var document = PatchParser.Parse(Encoding.UTF8.GetBytes("""
            {"schema":"hgss-save.patch/v1","operations":[{"op":"location.set","map":73,"x":1,"y":1,"z":3}]}
            """), "test");
        var error = Assert.Throws<HgssException>(() =>
            PatchPlanner.Plan(session, document, SymbolRegistry.Empty, "blank", session.OriginalBytes, [1], false, null, SafeSpawnsPath()));
        Assert.Equal(ExitCode.UnsafeOperation, error.ExitCode);
    }

    [Fact]
    public void PatchParserAcceptsPartyOps()
    {
        var doc = PatchParser.Parse(Encoding.UTF8.GetBytes("""
            {"schema":"hgss-save.patch/v1","operations":[
            {"op":"party.slot.import","slot":0,"file":"x.pk4"},
            {"op":"party.slot.clear","slot":1},
            {"op":"box.slot.import","box":0,"slot":0,"file":"y.pk4","expect_species":25},
            {"op":"box.slot.clear","box":1,"slot":2}]}
            """), "test");
        Assert.Equal(4, doc.Operations.Count);
    }

    [Fact]
    public void FixtureParserAcceptsRecipe()
    {
        var recipe = FixtureParser.Parse(Encoding.UTF8.GetBytes("""
            {"schema":"hgss-save.fixture/v1","id":"t","game":"SS",
            "base":{"path":"b.sav","sha256":"0000000000000000000000000000000000000000000000000000000000000000"},
            "patch":{"operations":[{"op":"trainer.money.set","value":1}]},
            "position":{"mode":"preserve-base"}}
            """), "test");
        Assert.Equal("t", recipe.Id);
        Assert.Single(recipe.Patch.Operations);
    }
}
