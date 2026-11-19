using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HgssSave.Core;
using HgssSave.PKHeX;
using Xunit;

namespace HgssSave.Tests;

/// <summary>
/// `story check --strict` and `story build --strict` must enforce proven invariants only, a rejected
/// build must not write anything, and a badge rewind must not pass as a coherent state.
/// Issues 1, 2 and 3.
/// </summary>
public sealed class StrictStoryTests
{
    private static readonly HgssFactory Factory = new();

    private static string Rules() =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "registry", "hgss-us.badge-rules.json");

    /// <summary>Path to the built CLI next to the test assembly, so exit codes are the real ones.</summary>
    private static string Exe()
    {
        var dir = AppContext.BaseDirectory;
        foreach (var candidate in new[] { "hgss-save.exe", "hgss-save" })
        {
            var path = Path.Combine(dir, candidate);
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"hgss-save not found beside {dir}");
    }

    private static byte[] Patch(byte[] bytes, string operation)
    {
        using var session = Factory.OpenBytes(bytes, "in");
        var doc = PatchParser.Parse(Encoding.UTF8.GetBytes(
            $$"""{"schema":"hgss-save.patch/v1","operations":[{{operation}}]}"""), "test");
        var plan = PatchPlanner.Plan(session, doc, SymbolRegistry.Empty, "in",
            session.OriginalBytes, [1], false, null, null, null, Rules());
        Assert.True(plan.CanApply, string.Join("; ", plan.PreconditionFailures.Concat(plan.Conflicts)));
        return PatchRunner.Apply(session, plan, Factory).OutputBytes;
    }

    private static byte[] Build(string operation) => Patch(SessionTests.BlankHgBytes(), operation);

    /// <summary>The HM01 give-flag set with no HM01 in the bag: one script sets both (state-log F12).</summary>
    private static byte[] HmFlagWithoutItem() =>
        Patch(Build("""{"op":"trainer.badges.set","johto":121}"""),
            """{"op":"flag.set","id":128,"value":true}""");

    /// <summary>
    /// HM01's item in the bag with no give-flag. Badge consequences never grant HM01, so unlike the
    /// reverse pair this contradiction survives a `story build` and reaches the audit.
    /// </summary>
    private static byte[] HmItemWithoutFlag() =>
        Patch(SessionTests.BlankHgBytes(),
            """{"op":"inventory.quantity.set","item":420,"quantity":1}""");

    private static string TempPath(string name) =>
        Path.Combine(Path.GetTempPath(), $"hgss-test-{Guid.NewGuid():N}-{name}");

    private static int CountField(string json, string name)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(name).GetInt32();
    }

    private static (int Exit, string Stdout) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Exe())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout);
    }

    // ----- issue 2: severity and evidence -----

    [Fact]
    public void HmFlagWithoutItemIsAnError()
    {
        var rules = BadgeRules.Load(Rules());
        using var s = Factory.OpenBytes(HmFlagWithoutItem(), "hm");
        var found = rules.Audit(s);
        var hm = Assert.Single(found, v => v.Rule == "hm-grant");
        Assert.Equal(BadgeRules.Severity.Error, hm.Severity);
        Assert.Equal(BadgeRules.Evidence.ScriptProven, hm.Evidence);
        Assert.True(BadgeRules.HasErrors(found));
    }

    /// <summary>Every corpus save with the HM also has the badge, but no script enforces that order.</summary>
    [Fact]
    public void ObservedOrderIsAWarningNotAnError()
    {
        var rules = BadgeRules.Load(Rules());
        using var s = Factory.OpenBytes(HmFlagWithoutItem(), "hm");
        var found = rules.Audit(s);
        var advisories = found.Where(v => v.Rule == "observed-order").ToList();
        Assert.NotEmpty(advisories);
        Assert.All(advisories, v =>
        {
            Assert.Equal(BadgeRules.Severity.Warning, v.Severity);
            Assert.Equal(BadgeRules.Evidence.CorpusCorrelated, v.Evidence);
            Assert.Equal("warning", v.SeverityName);
            Assert.Equal("corpus-correlated", v.EvidenceName);
        });
    }

    /// <summary>A raw badge byte with no consequences is a proven contradiction (state-log F8).</summary>
    [Fact]
    public void RawBadgeBytesAreAnError()
    {
        var rules = BadgeRules.Load(Rules());
        using var s = Factory.OpenBytes(Build("""{"op":"trainer.badges.set","johto":121}"""), "raw");
        var found = rules.Audit(s);
        var counter = Assert.Single(found, v => v.Rule == "counter");
        Assert.Equal(BadgeRules.Severity.Error, counter.Severity);
        Assert.Equal("error", counter.SeverityName);
        Assert.True(BadgeRules.HasErrors(found));
    }

    /// <summary>Strict `story check` fails on a proven contradiction and reports the split.</summary>
    [Fact]
    public void StoryCheckStrictFailsOnError()
    {
        var path = TempPath("raw.sav");
        try
        {
            File.WriteAllBytes(path, Build("""{"op":"trainer.badges.set","johto":121}"""));
            var (exit, stdout) = Run("story", "check", path, "--strict", "--badge-rules", Rules());
            Assert.Equal(6, exit); // ExitCode.PreconditionFailed
            Assert.Contains("\"severity\": \"error\"", stdout);
            // Mask 121 sets five badges with no consequences: the gym flags, both TMs and the
            // counter are all missing, so every finding is an error and none is a warning.
            var errors = CountField(stdout, "error_count");
            Assert.True(errors > 0, "strict failed but reported no errors");
            Assert.Equal(0, CountField(stdout, "warning_count"));
            Assert.Equal(CountField(stdout, "violation_count"), errors);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// Only corpus-correlated findings. Strict must pass: a legitimate but unusual progression is not
    /// a contradiction. Before issue 2 this returned PreconditionFailed.
    /// </summary>
    [Fact]
    public void WarningOnlySavePassesStrict()
    {
        var rules = BadgeRules.Load(Rules());
        // HM01 flag with the item, no badge behind it: hm-grant agrees, observed-order warns.
        var bytes = Patch(Patch(SessionTests.BlankHgBytes(),
                """{"op":"inventory.quantity.set","item":420,"quantity":1}"""),
            """{"op":"flag.set","id":128,"value":true}""");
        using var s = Factory.OpenBytes(bytes, "hm");
        var found = rules.Audit(s);
        Assert.DoesNotContain(found, v => v.Rule == "hm-grant");
        Assert.Contains(found, v => v.Rule == "observed-order");
        Assert.False(BadgeRules.HasErrors(found));

        var path = TempPath("warn.sav");
        try
        {
            File.WriteAllBytes(path, bytes);
            var (exit, stdout) = Run("story", "check", path, "--strict", "--badge-rules", Rules());
            Assert.Equal(0, exit);
            Assert.True(CountField(stdout, "warning_count") > 0);
            Assert.Equal(0, CountField(stdout, "error_count"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ----- issue 1: no output on a rejected strict build -----

    /// <summary>A successful strict build writes the output and exits 0.</summary>
    [Fact]
    public void SuccessfulStrictBuildWritesOutput()
    {
        var input = TempPath("in.sav");
        var output = TempPath("out.sav");
        try
        {
            File.WriteAllBytes(input, Build("""{"op":"trainer.badges.set","johto":1,"consequences":true}"""));
            var (exit, stdout) = Run("story", "build", input, "--johto", "1",
                "--out", output, "--strict", "--badge-rules", Rules());
            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));
            Assert.Equal(0x80000, new FileInfo(output).Length);
            Assert.Contains("\"written\": true", stdout);
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>
    /// Issue 1: a build rejected by the audit must not create the output. The input owns HM01's item
    /// without its give-flag, and no badge consequence repairs that pair, so the rewind guard is opted
    /// out of and the audit is what rejects the build.
    /// </summary>
    [Fact]
    public void AuditRejectedBuildLeavesNoOutput()
    {
        var input = TempPath("in.sav");
        var output = TempPath("out.sav");
        try
        {
            File.WriteAllBytes(input, HmItemWithoutFlag());
            var (exit, stdout) = Run("story", "build", input, "--johto", "0", "--kanto", "0",
                "--out", output, "--use-input-as-base", "--strict", "--badge-rules", Rules());
            Assert.Equal(6, exit);
            Assert.Contains("\"written\": false", stdout);
            Assert.True(CountField(stdout, "error_count") > 0);
            Assert.False(File.Exists(output), "a rejected strict build left bytes at --out");
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>Issue 1: --force must not destroy an existing destination when the audit rejects.</summary>
    [Fact]
    public void AuditRejectedBuildPreservesExistingDestination()
    {
        var input = TempPath("in.sav");
        var output = TempPath("out.sav");
        try
        {
            File.WriteAllBytes(input, HmItemWithoutFlag());
            var sentinel = new byte[] { 1, 2, 3, 4, 5 };
            File.WriteAllBytes(output, sentinel);
            var (exit, _) = Run("story", "build", input, "--johto", "0", "--kanto", "0",
                "--out", output, "--use-input-as-base", "--strict", "--force", "--badge-rules", Rules());
            Assert.Equal(6, exit);
            Assert.Equal(sentinel, File.ReadAllBytes(output));
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    // ----- issue 3: never present a rewound save as coherent -----

    /// <summary>
    /// A late-game input asking for zero badges must refuse: the preset would lower the badge byte and
    /// leave the postgame flags in place. Before issue 3 this produced an apparently clean start.
    /// </summary>
    [Fact]
    public void LoweringBadgesOnLateGameInputIsRefused()
    {
        var input = TempPath("late.sav");
        var output = TempPath("out.sav");
        try
        {
            File.WriteAllBytes(input, Build("""{"op":"trainer.badges.set","johto":121,"consequences":true}"""));
            var (exit, stdout) = Run("story", "build", input, "--johto", "0", "--kanto", "0",
                "--out", output, "--badge-rules", Rules());
            Assert.Equal(2, exit); // ExitCode.BadArguments
            Assert.Contains("no-coherent-base-preset", stdout);
            Assert.False(File.Exists(output), "a refused build wrote output");
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>The same request with the opt-in succeeds but says the result is not coherent.</summary>
    [Fact]
    public void OptInBuildIsLabelledPartial()
    {
        var input = TempPath("late.sav");
        var output = TempPath("out.sav");
        try
        {
            File.WriteAllBytes(input, Build("""{"op":"trainer.badges.set","johto":121,"consequences":true}"""));
            var (exit, stdout) = Run("story", "build", input, "--johto", "0", "--kanto", "0",
                "--out", output, "--use-input-as-base", "--badge-rules", Rules());
            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));
            Assert.Contains("\"coherent\": false", stdout);
            Assert.Contains("\"rewinds_input_badges\": true", stdout);
            Assert.Contains("PARTIAL", stdout);
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>A request the presets can satisfy stays coherent and passes strict.</summary>
    [Fact]
    public void SupportedMilestoneStaysCoherent()
    {
        var input = TempPath("mid.sav");
        var output = TempPath("out.sav");
        try
        {
            File.WriteAllBytes(input, Build("""{"op":"trainer.badges.set","johto":3,"consequences":true}"""));
            var (exit, stdout) = Run("story", "build", input, "--johto", "7",
                "--out", output, "--strict", "--badge-rules", Rules());
            Assert.Equal(0, exit);
            Assert.Contains("\"coherent\": true", stdout);
            Assert.Contains("\"written\": true", stdout);
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
