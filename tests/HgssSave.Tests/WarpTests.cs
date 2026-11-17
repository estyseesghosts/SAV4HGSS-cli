using System.IO;
using System.Text;
using HgssSave.Core;
using HgssSave.PKHeX;
using Xunit;

namespace HgssSave.Tests;

/// <summary>location.warp: flag 0x966 + saved dynamic warp (docs/state-log.md F4/F5).</summary>
public sealed class WarpTests
{
    private static readonly HgssFactory Factory = new();

    private const string WarpTable = """
        {"schema":"hgss-save.warp-spawns/v1","maps":[
          {"map_id":73,"map_symbol":"MAP_VIOLET","warp_arrivals":[
            {"warp_index":2,"x":479,"y":257,"destination_symbol":"MAP_VIOLET_GYM"}]}]}
        """;

    private static string TableFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"warp-table-{System.Guid.NewGuid():N}.json");
        File.WriteAllText(path, WarpTable);
        return path;
    }

    private static PatchDocument Doc(string operation) => PatchParser.Parse(Encoding.UTF8.GetBytes(
        $$"""{"schema":"hgss-save.patch/v1","operations":[{{operation}}]}"""), "test");

    [Fact]
    public void ParserReadsWarpOperationAndDefaultsDirection()
    {
        var op = Assert.IsType<LocationWarpOperation>(Assert.Single(Doc("""{"op":"location.warp","map":73,"warp":2}""").Operations));
        Assert.Equal((73, 2, 1), (op.Map, op.Warp, op.Direction));
    }

    [Fact]
    public void ParserRejectsBadWarpFields()
    {
        Assert.Throws<HgssException>(() => Doc("""{"op":"location.warp","map":73,"warp":2,"direction":4}"""));
        Assert.Throws<HgssException>(() => Doc("""{"op":"location.warp","map":73,"warp":2,"x":1}"""));
        Assert.Throws<HgssException>(() => Doc("""{"op":"location.warp","map":73}"""));
    }

    [Fact]
    public void UnlistedWarpIsRefusedAsUnsafe()
    {
        var table = TableFile();
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        var error = Assert.Throws<HgssException>(() => PatchPlanner.Plan(
            session, Doc("""{"op":"location.warp","map":73,"warp":9}"""), SymbolRegistry.Empty, "blank",
            session.OriginalBytes, [1], false, null, null, table));
        Assert.Equal(ExitCode.UnsafeOperation, error.ExitCode);
    }

    [Fact]
    public void WarpWritesDynamicWarpAndContinueFlag()
    {
        var table = TableFile();
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        Assert.True(session.TryGetFlag(0x966, out var before) && !before);

        var plan = PatchPlanner.Plan(session, Doc("""{"op":"location.warp","map":73,"warp":2,"direction":3}"""),
            SymbolRegistry.Empty, "blank", session.OriginalBytes, [1], false, null, null, table);
        Assert.True(plan.CanApply);
        var result = PatchRunner.Apply(session, plan, Factory);

        Assert.True(result.Audit.Accepted);
        Assert.Equal(0, result.Audit.UnclassifiedBytes);
        Assert.Empty(result.PostconditionFailures);
        Assert.True(result.Validation.Passed);

        using var reopened = Factory.OpenBytes(result.OutputBytes, "out");
        Assert.Equal(new DynamicWarpSnapshot(73, 2, 479, 257, 3), reopened.DynamicWarp);
        Assert.True(reopened.TryGetFlag(0x966, out var after) && after);
    }
}
