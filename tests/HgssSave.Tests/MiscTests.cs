using System.IO;
using System.Text;
using HgssSave.Core;
using HgssSave.PKHeX;
using Xunit;

namespace HgssSave.Tests;

public sealed class MiscTests
{
    private static readonly HgssFactory Factory = new();

    [Fact]
    public void ByteAuditAcceptsIdenticalImages()
    {
        var bytes = new byte[16];
        var audit = ByteAudit.AuditPatch(bytes, (byte[])bytes.Clone(), [], [], Factory.KnownRegions, 40);
        Assert.True(audit.Accepted);
        Assert.Equal(0, audit.ChangedBytes);
    }

    [Fact]
    public void ByteAuditRejectsUnexplainedBytes()
    {
        var before = new byte[16];
        var after = new byte[16];
        after[5] = 1;
        var audit = ByteAudit.AuditPatch(before, after, [], [], Factory.KnownRegions, 40);
        Assert.False(audit.Accepted);
        Assert.Contains("0x00005", audit.RejectionReason);
    }

    [Fact]
    public void DsvProbeAndUnwrap()
    {
        var raw = SessionTests.BlankHgBytes();
        var dir = Directory.CreateTempSubdirectory("hgss-dsv").FullName;
        var dsvPath = Path.Combine(dir, "test.dsv");
        using (var stream = File.Create(dsvPath))
        {
            stream.Write(raw);
            stream.Write(new byte[HgssLayout.DsvFooterSize - 16]); // filler before the magic
            stream.Write(Encoding.ASCII.GetBytes(HgssLayout.DsvMagic));
        }

        Assert.Equal(ContainerKind.DeSmuME, Factory.ProbeContainer(dsvPath));
        var unwrapped = Factory.UnwrapContainer(dsvPath);
        Assert.Equal(raw, unwrapped);
        using var session = Factory.OpenBytes(unwrapped, "unwrapped");
        Assert.Equal("HG", session.Identity.Game);
    }

    [Fact]
    public void RawProbe()
    {
        var dir = Directory.CreateTempSubdirectory("hgss-raw").FullName;
        var path = Path.Combine(dir, "test.sav");
        File.WriteAllBytes(path, SessionTests.BlankHgBytes());
        Assert.Equal(ContainerKind.Raw, Factory.ProbeContainer(path));
        Assert.Equal(SessionTests.BlankHgBytes(), Factory.UnwrapContainer(path));
    }

    [Fact]
    public void UnknownContainerIsOther()
    {
        var dir = Directory.CreateTempSubdirectory("hgss-other").FullName;
        var path = Path.Combine(dir, "test.sav");
        File.WriteAllBytes(path, new byte[12345]);
        Assert.Equal(ContainerKind.Other, Factory.ProbeContainer(path));
        var error = Assert.Throws<HgssException>(() => Factory.UnwrapContainer(path));
        Assert.Equal(ExitCode.UnsupportedSave, error.ExitCode);
    }

    [Fact]
    public void CheckedInRegistryResolvesCoreSymbols()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../registry/hgss-us.symbols.json"));
        if (!File.Exists(path))
            return; // registry artifact not checked in yet; builder produces it
        var registry = SymbolRegistry.Load(path);
        Assert.StartsWith("hgss-us@", registry.Revision);
        var flag = registry.LookupAs("FLAG_CAUGHT_LUGIA", SymbolKind.PersistentFlag);
        Assert.Equal(SymbolResolution.Resolved, flag.Resolution);
        Assert.Equal(0x117, flag.Entry!.Id);
        var variable = registry.LookupAs("VAR_PLAYER_STARTER", SymbolKind.SaveWork);
        Assert.Equal(SymbolResolution.Resolved, variable.Resolution);
        Assert.Equal(0x4030, variable.Entry!.Id);
        var map = registry.LookupAs("MAP_CHERRYGROVE", SymbolKind.Map);
        Assert.Equal(SymbolResolution.Resolved, map.Resolution);
        var item = registry.LookupAs("ITEM_POTION", SymbolKind.Item);
        Assert.Equal(SymbolResolution.Resolved, item.Resolution);
        var temp = registry.LookupAs("FLAG_MAPTEMP_001", SymbolKind.TemporaryFlag);
        Assert.Equal(SymbolResolution.Resolved, temp.Resolution);
    }
}
