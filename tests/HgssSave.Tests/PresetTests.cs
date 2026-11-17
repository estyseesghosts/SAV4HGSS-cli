using System.IO;
using System.Text;
using HgssSave.Core;
using HgssSave.PKHeX;
using Xunit;

namespace HgssSave.Tests;

/// <summary>Registry presets (registry/presets) parse and apply as ordinary recipes.</summary>
public sealed class PresetTests
{
    private static readonly HgssFactory Factory = new();

    private static string PresetDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "registry", "presets")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("registry/presets"), "registry", "presets");
    }

    [Fact]
    public void EveryIndexedPresetParses()
    {
        var dir = PresetDir();
        var index = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "index.json")));
        var count = 0;
        foreach (var p in index.RootElement.GetProperty("presets").EnumerateArray())
        {
            var path = Path.Combine(dir, p.GetProperty("name").GetString() + ".json");
            var doc = PatchParser.Parse(File.ReadAllBytes(path), path);
            Assert.Equal(p.GetProperty("operations").GetInt32(), doc.Operations.Count);
            count++;
        }

        Assert.True(count >= 10);
    }

    [Fact]
    public void FogPresetAppliesToBlankHgAndSetsBadgesAndSpawn()
    {
        var path = Path.Combine(PresetDir(), "hg-04-fog.json");
        var doc = PatchParser.Parse(File.ReadAllBytes(path), path);
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        var plan = PatchPlanner.Plan(session, doc, SymbolRegistry.Empty, "blank", session.OriginalBytes, [1], false, null, null,
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "registry", "hgss-us.warp-spawns.json"));
        Assert.True(plan.CanApply);
        var result = PatchRunner.Apply(session, plan, Factory);
        Assert.True(result.Audit.Accepted);
        Assert.Empty(result.PostconditionFailures);
        using var reopened = Factory.OpenBytes(result.OutputBytes, "out");
        Assert.Equal(15, reopened.Snapshot().Trainer.JohtoBadges);
        Assert.Equal(78, reopened.DynamicWarp!.MapId);
    }
}
