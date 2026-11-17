using System.Text;
using HgssSave.Core;
using HgssSave.PKHeX;
using Xunit;

namespace HgssSave.Tests;

/// <summary>trainer.badges.set: Johto/Kanto badge bytes in the trainer block.</summary>
public sealed class BadgeTests
{
    private static readonly HgssFactory Factory = new();

    private static PatchDocument Doc(string operation) => PatchParser.Parse(Encoding.UTF8.GetBytes(
        $$"""{"schema":"hgss-save.patch/v1","operations":[{{operation}}]}"""), "test");

    [Fact]
    public void ParserNeedsAtLeastOneMaskInRange()
    {
        Assert.Throws<HgssException>(() => Doc("""{"op":"trainer.badges.set"}"""));
        Assert.Throws<HgssException>(() => Doc("""{"op":"trainer.badges.set","johto":256}"""));
        var op = Assert.IsType<BadgesSetOperation>(Assert.Single(Doc("""{"op":"trainer.badges.set","kanto":15}""").Operations));
        Assert.Equal((null, 15), (op.Johto, op.Kanto));
    }

    [Fact]
    public void BadgesWriteAndRoundTrip()
    {
        using var session = Factory.OpenBytes(SessionTests.BlankHgBytes(), "blank");
        var plan = PatchPlanner.Plan(session, Doc("""{"op":"trainer.badges.set","johto":255,"kanto":3}"""),
            SymbolRegistry.Empty, "blank", session.OriginalBytes, [1], false, null, null, null);
        Assert.True(plan.CanApply);
        var result = PatchRunner.Apply(session, plan, Factory);
        Assert.True(result.Audit.Accepted);
        Assert.Equal(0, result.Audit.UnclassifiedBytes);
        Assert.Empty(result.PostconditionFailures);

        using var reopened = Factory.OpenBytes(result.OutputBytes, "out");
        var t = reopened.Snapshot().Trainer;
        Assert.Equal((255, 3), (t.JohtoBadges, t.KantoBadges));
    }
}

/// <summary>trainer.badges.set with consequences (registry/hgss-us.badge-rules.json).</summary>
public sealed class BadgeConsequenceTests
{
    private static readonly HgssFactory Factory = new();
    private static readonly string Rules = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "registry", "hgss-us.badge-rules.json");

    private static PatchDocument Doc(string operation) => PatchParser.Parse(Encoding.UTF8.GetBytes(
        $$"""{"schema":"hgss-save.patch/v1","operations":[{{operation}}]}"""), "test");

    private static PatchResult Run(PatchDocument doc, byte[]? bytes = null)
    {
        using var session = Factory.OpenBytes(bytes ?? SessionTests.BlankHgBytes(), "blank");
        var plan = PatchPlanner.Plan(session, doc, SymbolRegistry.Empty, "blank", session.OriginalBytes, [1], false, null, null, null, Rules);
        Assert.True(plan.CanApply, string.Join("; ", plan.PreconditionFailures.Concat(plan.Conflicts)));
        return PatchRunner.Apply(session, plan, Factory);
    }

    [Fact]
    public void ConsequencesAreOffUnlessAsked()
    {
        var result = Run(Doc("""{"op":"trainer.badges.set","johto":1}"""));
        using var reopened = Factory.OpenBytes(result.OutputBytes, "out");
        Assert.True(reopened.TryGetFlag(0x192 + 0, out var f) && !f);
    }

    [Fact]
    public void GainedBadgesWriteGymFlagsTmAndMidgameCounter()
    {
        // Zephyr, Fog, Storm, Mineral, Glacier = bits 0,3,4,5,6
        var result = Run(Doc("""{"op":"trainer.badges.set","johto":121,"consequences":true}"""));
        Assert.True(result.Audit.Accepted);
        Assert.Equal(0, result.Audit.UnclassifiedBytes);
        using var reopened = Factory.OpenBytes(result.OutputBytes, "out");
        Assert.True(reopened.TryGetFlag(0x192, out var gym) && gym);           // FLAG_UNK_192, Zephyr script
        Assert.True(reopened.TryGetBagQuantity(378, out var tm51, out _) && tm51 == 1); // TM51 from Falkner
        Assert.True(reopened.TryGetBagQuantity(357, out var tm30, out _) && tm30 == 1); // TM30 from Morty
        Assert.True(reopened.TryGetVar(0x4134, out var midgame) && midgame == 4);       // Fog+Storm+Mineral+Glacier
    }
}
