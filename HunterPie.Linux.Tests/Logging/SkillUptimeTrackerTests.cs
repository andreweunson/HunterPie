using HunterPie.Core.Game.Entity.Player;
using HunterPie.Core.Game.Enums;
using HunterPie.Linux.Logging;
using NSubstitute;

namespace HunterPie.Linux.Tests.Logging;

[TestClass]
public class SkillUptimeTrackerTests
{
    private static IAbnormality Skill(string id, AbnormalityType type = AbnormalityType.Skill)
    {
        IAbnormality abnormality = Substitute.For<IAbnormality>();
        abnormality.Id.Returns(id);
        abnormality.Name.Returns(string.Empty);
        abnormality.Type.Returns(type);
        return abnormality;
    }

    private static string Row(IEnumerable<string> summary, string id) =>
        summary.Single(it => it.TrimStart().StartsWith(id, StringComparison.Ordinal));

    [TestMethod]
    public void HalfTheQuestActiveIsFiftyPercent()
    {
        var tracker = new SkillUptimeTracker();
        IAbnormality agitator = Skill("Agitator");

        tracker.Begin(0, []);
        tracker.Start(agitator, 0);
        tracker.End(agitator, 30);
        tracker.CloseAll(60);

        string row = Row(tracker.Summary(), "Agitator");

        StringAssert.Contains(row, "50.0%");
        StringAssert.Contains(row, "(00:30, 1 activation)");
    }

    [TestMethod]
    public void SeveralActivationsAddUp()
    {
        var tracker = new SkillUptimeTracker();
        IAbnormality burst = Skill("Burst");

        tracker.Begin(0, []);
        tracker.Start(burst, 10);
        tracker.End(burst, 20);
        tracker.Start(burst, 40);
        tracker.End(burst, 50);
        tracker.CloseAll(100);

        string row = Row(tracker.Summary(), "Burst");

        StringAssert.Contains(row, "20.0%");
        StringAssert.Contains(row, "2 activations");
    }

    [TestMethod]
    public void RepeatedStartIsTheSameActivation()
    {
        var tracker = new SkillUptimeTracker();
        IAbnormality burst = Skill("Burst");

        tracker.Begin(0, []);
        tracker.Start(burst, 10);
        tracker.Start(burst, 15);
        tracker.End(burst, 20);
        tracker.CloseAll(100);

        string row = Row(tracker.Summary(), "Burst");

        StringAssert.Contains(row, "10.0%");
        StringAssert.Contains(row, "1 activation");
    }

    [TestMethod]
    public void SkillThatIsStillRunningIsClosedAtTheEndOfTheQuest()
    {
        var tracker = new SkillUptimeTracker();

        tracker.Begin(0, []);
        tracker.Start(Skill("Counterstrike"), 50);
        tracker.CloseAll(100);

        StringAssert.Contains(Row(tracker.Summary(), "Counterstrike"), "50.0%");
    }

    [TestMethod]
    public void SkillsAlreadyActiveWhenTheQuestStartsCountFromTheStart()
    {
        var tracker = new SkillUptimeTracker();

        tracker.Begin(0, [Skill("Latent")]);
        tracker.CloseAll(60);

        StringAssert.Contains(Row(tracker.Summary(), "Latent"), "100.0%");
    }

    [TestMethod]
    public void OnlySkillsAreCounted()
    {
        var tracker = new SkillUptimeTracker();

        tracker.Begin(0, []);
        tracker.Start(Skill("Dango", AbnormalityType.Consumable), 0);
        tracker.CloseAll(60);

        string[] summary = [.. tracker.Summary()];

        Assert.IsFalse(summary.Any(it => it.Contains("Dango", StringComparison.Ordinal)));
        Assert.IsTrue(summary.Any(it => it.Contains("no timed skills", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void EndWithoutStartIsIgnored()
    {
        var tracker = new SkillUptimeTracker();

        tracker.Begin(0, []);
        tracker.End(Skill("Ghost"), 10);
        tracker.CloseAll(60);

        Assert.IsFalse(tracker.Summary().Any(it => it.Contains("Ghost", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void UptimeNeverGoesOverOneHundredPercent()
    {
        var tracker = new SkillUptimeTracker();
        IAbnormality skill = Skill("Overlap");

        tracker.Begin(0, []);
        tracker.Start(skill, 0);
        tracker.End(skill, 60.4);
        tracker.CloseAll(60);

        StringAssert.Contains(Row(tracker.Summary(), "Overlap"), "100.0%");
    }

    [TestMethod]
    public void AVeryShortQuestIsNotMeasured()
    {
        var tracker = new SkillUptimeTracker();

        tracker.Begin(0, []);
        tracker.CloseAll(0.5);

        Assert.IsTrue(tracker.Summary().Any(it => it.Contains("too short", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AttachingLateIsSaidInTheSummary()
    {
        var tracker = new SkillUptimeTracker();

        tracker.Begin(HuntRecorder.EarlyQuestSeconds + 100, []);
        tracker.CloseAll(HuntRecorder.EarlyQuestSeconds + 200);

        Assert.IsTrue(tracker.Summary().Any(it => it.Contains("when HunterPie attached", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ANewHuntForgetsTheLastOne()
    {
        var tracker = new SkillUptimeTracker();

        tracker.Begin(0, [Skill("Old")]);
        tracker.CloseAll(60);
        tracker.Begin(0, []);
        tracker.CloseAll(60);

        Assert.IsFalse(tracker.Summary().Any(it => it.Contains("Old", StringComparison.Ordinal)));
    }
}
