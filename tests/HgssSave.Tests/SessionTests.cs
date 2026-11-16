using HgssSave.Core;
using HgssSave.PKHeX;
using PKHeX.Core;
using Xunit;

namespace HgssSave.Tests;

public sealed class SessionTests
{
    private static readonly HgssFactory Factory = new();

    internal static byte[] BlankHgBytes() =>
        Factory.SynthesizeBlank(BlankKind.FreshStructure, new BlankOptions("HG", null, null, null, null, null));

    internal static ISaveSession OpenBlankHg() =>
        Factory.OpenBytes(BlankHgBytes(), "blank-hg");

    [Fact]
    public void BlankOpensWithHgIdentity()
    {
        using var session = OpenBlankHg();
        Assert.Equal("HG", session.Identity.Game);
        Assert.Equal(0x80000, session.Identity.Bytes);
        Assert.True(session.ValidateStructural().Passed);
    }

    [Fact]
    public void BlankSsOpensWithSsIdentity()
    {
        var bytes = Factory.SynthesizeBlank(BlankKind.FreshStructure, new BlankOptions("SS", "Lyra", 1, 2, 1, 2));
        using var session = Factory.OpenBytes(bytes, "blank-ss");
        Assert.Equal("SS", session.Identity.Game);
        Assert.Equal("Lyra", session.Trainer.Name);
        Assert.True(session.ValidateStructural().Passed);
    }

    [Fact]
    public void ErasedFlashIsRejectedAsUnsupported()
    {
        var bytes = Factory.SynthesizeBlank(BlankKind.ErasedFlash, new BlankOptions("HG", null, null, null, null, null));
        var error = Assert.Throws<HgssException>(() => Factory.OpenBytes(bytes, "erased"));
        Assert.Equal(ExitCode.UnsupportedSave, error.ExitCode);
    }

    [Fact]
    public void ZeroedImageIsRejectedAsUnsupported()
    {
        var error = Assert.Throws<HgssException>(() => Factory.OpenBytes(new byte[0x80000], "zeros"));
        Assert.Equal(ExitCode.UnsupportedSave, error.ExitCode);
    }

    [Fact]
    public void WrongSizeIsRejectedAsUnsupported()
    {
        var error = Assert.Throws<HgssException>(() => Factory.OpenBytes(new byte[100], "tiny"));
        Assert.Equal(ExitCode.UnsupportedSave, error.ExitCode);
    }

    [Fact]
    public void BadGameIsRejectedAsUnsupported()
    {
        var bytes = BlankHgBytes();
        bytes[0x80] = 10; // ROMCode at Trainer1+0x1C; checksums fail too, but identity is checked first
        var error = Assert.Throws<HgssException>(() => Factory.OpenBytes(bytes, "bad-game"));
        Assert.Equal(ExitCode.UnsupportedSave, error.ExitCode);
    }

    [Fact]
    public void CorruptedChecksumIsRejectedAsStructural()
    {
        var bytes = BlankHgBytes();
        bytes[0x100] ^= 0xFF; // active general data; footer CRC no longer matches
        var error = Assert.Throws<HgssException>(() => Factory.OpenBytes(bytes, "corrupt"));
        Assert.Equal(ExitCode.StructuralFailure, error.ExitCode);
    }

    [Fact]
    public void FlagRoundTripSurvivesExport()
    {
        using var session = OpenBlankHg();
        Assert.False(session.TryGetFlag(279, out var before) && before);
        session.SetFlag(279, true);
        var export = session.Export();
        Assert.True(export.ChecksumsValid);
        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        Assert.True(reparsed.TryGetFlag(279, out var after) && after);
    }

    [Fact]
    public void VarRoundTripSurvivesExport()
    {
        using var session = OpenBlankHg();
        session.SetVar(0x4030, 42);
        var export = session.Export();
        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        Assert.True(reparsed.TryGetVar(0x4030, out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void FlagOutOfRangeIsRejected()
    {
        using var session = OpenBlankHg();
        Assert.False(session.TryGetFlag(2912, out _));
        Assert.Throws<HgssException>(() => session.SetFlag(2912, true));
    }

    [Fact]
    public void VarOutOfRangeIsRejected()
    {
        using var session = OpenBlankHg();
        Assert.False(session.TryGetVar(0x4000 - 1, out _));
        Assert.False(session.TryGetVar(0x4000 + 368, out _));
        Assert.Throws<HgssException>(() => session.SetVar(0x4000 + 368, 1));
    }

    [Fact]
    public void MoneyRoundTripSurvivesExport()
    {
        using var session = OpenBlankHg();
        session.SetMoney(50000);
        var export = session.Export();
        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        Assert.Equal(50000u, reparsed.Trainer.Money);
    }

    [Fact]
    public void MoneyAboveMaximumIsRejected()
    {
        using var session = OpenBlankHg();
        var error = Assert.Throws<HgssException>(() => session.SetMoney(1_000_000));
        Assert.Equal(ExitCode.PreconditionFailed, error.ExitCode);
    }

    [Fact]
    public void BagSetRoundTripSurvivesExport()
    {
        using var session = OpenBlankHg();
        session.SetBagQuantity(17, 5); // Potion, medicine pocket
        var export = session.Export();
        Assert.True(export.ChecksumsValid);
        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        Assert.True(reparsed.TryGetBagQuantity(17, out var quantity, out _));
        Assert.Equal(5, quantity);
    }

    [Fact]
    public void BagHmQuantityAboveOneIsRejected()
    {
        using var session = OpenBlankHg();
        var error = Assert.Throws<HgssException>(() => session.SetBagQuantity(420, 5)); // HM01, max 1
        Assert.Equal(ExitCode.PreconditionFailed, error.ExitCode);
    }

    [Fact]
    public void BagUnknownItemIsRejected()
    {
        using var session = OpenBlankHg();
        var error = Assert.Throws<HgssException>(() => session.SetBagQuantity(9999, 1));
        Assert.Equal(ExitCode.UnsafeOperation, error.ExitCode);
    }

    [Fact]
    public void GeneralAndKeyPocketsDoNotAlias()
    {
        // GeneralPt lists 192 ids for a 165-slot physical pocket; KeyItems live at the
        // same file offsets. Both edits must survive one CopyTo round-trip.
        using var session = OpenBlankHg();
        session.SetBagQuantity(68, 3); // general pocket
        session.SetBagQuantity(434, 1); // HGSS key item
        var export = session.Export();
        using var reparsed = Factory.OpenBytes(export.Bytes, "reparse");
        Assert.True(reparsed.TryGetBagQuantity(68, out var general, out _));
        Assert.True(reparsed.TryGetBagQuantity(434, out var key, out _));
        Assert.Equal(3, general);
        Assert.Equal(1, key);
    }

    [Fact]
    public void LayoutConstantsMatchPkhexBehavior()
    {
        Assert.Equal(SAV4HGSS.GeneralSize, HgssLayout.GeneralSize);
        using var session = OpenBlankHg();
        Assert.Equal(0xB60, session.FlagCount);
        Assert.True(session.WorkCount == 0x170);
        Assert.Equal(999999u, session.Trainer.MaxMoney);
        var report = session.Preflight;
        Assert.True(PreflightActiveValid(report));
    }

    private static bool PreflightActiveValid(PreflightReport report)
    {
        foreach (var copy in report.Copies)
        {
            var active = (copy.Block == "general" && copy.Partition == report.ActiveGeneral)
                || (copy.Block == "storage" && copy.Partition == report.ActiveStorage);
            if (active && (!copy.Initialized || !copy.CrcValid))
                return false;
        }

        return true;
    }
}
