using System;
using System.Linq;
using HgssSave.Core;
using HgssSave.PKHeX;
using Xunit;

namespace HgssSave.Tests;

public sealed class PartyCreateTests
{
    private static readonly HgssFactory Factory = new();

    [Fact]
    public void CreateUsesTheSpeciesLevelUpMoveset()
    {
        var (bytes, result) = Pk4Builder.Create(155, 20, null, "STAGE", 4242, 1717, 2, null);

        Assert.Equal(Pk4Builder.PartyBytes, bytes.Length);
        Assert.True(result.ChecksumValid);
        Assert.Equal(155, result.Species);
        Assert.Equal("Cyndaquil", result.SpeciesName);
        Assert.Equal("level-up", result.MovesSource);
        Assert.All(result.Moves, move => Assert.NotEqual(0, move)); // a level-20 starter knows four moves
        // The old out-of-tree harness hard-coded Typhlosion's set, so every species led with Flamethrower.
        Assert.DoesNotContain(53, result.Moves);
    }

    [Fact]
    public void ExplicitMovesAreWrittenInSlotOrder()
    {
        var (_, result) = Pk4Builder.Create(157, 100, [89, 247, 53, 7], "TEREZI", 4242, 1717, 2, null);

        Assert.Equal("explicit", result.MovesSource);
        Assert.Equal([89, 247, 53, 7], result.Moves);
        Assert.Equal(["Earthquake", "Shadow Ball", "Flamethrower", "Fire Punch"], result.MoveNames);
    }

    [Fact]
    public void CreateIsDeterministic()
    {
        var first = Pk4Builder.Create(157, 100, null, "STAGE", 4242, 1717, 2, null).Bytes;
        var second = Pk4Builder.Create(157, 100, null, "STAGE", 4242, 1717, 2, null).Bytes;

        Assert.Equal(HashUtil.Sha256(first), HashUtil.Sha256(second));
    }

    [Fact]
    public void GeneratedPartyImportsIntoASave()
    {
        var (bytes, result) = Pk4Builder.Create(157, 100, [53, 205, 38, 284], "STAGE", 4242, 1717, 2, null);

        // The same validation `party import` runs, before any save is involved.
        var preview = HgssSession.PreviewPkBytes(bytes);
        Assert.Equal(157, preview.Species);
        Assert.True(preview.ChecksumValid);

        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        session.ImportPartySlot(0, bytes, updateDex: true);
        var export = session.Export();
        Assert.True(export.ChecksumsValid);

        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        var slot = reparsed.Snapshot().Party[0];
        Assert.True(slot.Present);
        Assert.Equal(157, slot.Species);
        Assert.Equal(100, slot.Level);
        Assert.Equal(result.Moves, slot.Moves);
    }

    [Theory]
    [InlineData(0, 100, "species-range")]
    [InlineData(494, 100, "species-range")]
    [InlineData(157, 0, "level-range")]
    [InlineData(157, 101, "level-range")]
    public void RejectsSpeciesAndLevelOutsideTheSaveRange(int species, int level, string diagnostic)
    {
        var error = Assert.Throws<HgssException>(() =>
            Pk4Builder.Create(species, level, null, "STAGE", 4242, 1717, 2, null));

        Assert.Equal(diagnostic, error.DiagnosticId);
        Assert.Equal(ExitCode.BadArguments, error.ExitCode);
    }

    [Fact]
    public void RejectsMoreThanFourMoves()
    {
        var error = Assert.Throws<HgssException>(() =>
            Pk4Builder.Create(157, 100, [1, 2, 3, 4, 5], "STAGE", 4242, 1717, 2, null));

        Assert.Equal("moves-range", error.DiagnosticId);
    }

    [Fact]
    public void RejectsMovesOutsideTheGenFourRange()
    {
        var error = Assert.Throws<HgssException>(() =>
            Pk4Builder.Create(157, 100, [468], "STAGE", 4242, 1717, 2, null));

        Assert.Equal("moves-range", error.DiagnosticId);
    }

    [Theory]
    [InlineData("", "name-empty")]
    [InlineData("TOOLONGNA", "name-length")]
    public void RejectsNamesTheGenFourFieldCannotHold(string ot, string diagnostic)
    {
        var error = Assert.Throws<HgssException>(() =>
            Pk4Builder.Create(157, 100, null, ot, 4242, 1717, 2, null));

        Assert.Equal(diagnostic, error.DiagnosticId);
    }

    [Fact]
    public void NicknameIsOnlySetWhenAskedFor()
    {
        var (_, plain) = Pk4Builder.Create(25, 20, null, "STAGE", 4242, 1717, 2, null);
        var (_, nicknamed) = Pk4Builder.Create(25, 20, null, "STAGE", 4242, 1717, 2, "SPARKY");

        Assert.False(plain.Nicknamed);
        Assert.Equal("Pikachu", plain.SpeciesName);
        Assert.True(nicknamed.Nicknamed);
    }
}
