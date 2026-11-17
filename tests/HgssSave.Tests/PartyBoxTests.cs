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
