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
