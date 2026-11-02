using HunterPie.Core.Client.Configuration.Enums;
using HunterPie.Core.Game.Enums;
using HunterPie.Linux.Overlay.Services;

namespace HunterPie.Linux.Tests.Overlay;

[TestClass]
public class MonsterTargetSelectorTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static TargetCandidate C(string key, double dist, double health, double hitSecondsAgo = 100) =>
        new(key, dist, Now.AddSeconds(-hitSecondsAgo), health);

    [TestMethod]
    public void Infer_PrefersTheCloserMonster()
    {
        Assert.AreEqual("near", MonsterTargetSelector.Infer([C("far", 60, 0.5), C("near", 10, 0.5)], Now));
    }

    [TestMethod]
    public void Infer_PrefersRecentlyHitMonster()
    {
        Assert.AreEqual("hit", MonsterTargetSelector.Infer([C("idle", 30, 0.5), C("hit", 30, 0.5, 1)], Now));
    }

    [TestMethod]
    public void Infer_IgnoresDeadAndFarHealthyMonsters()
    {
        Assert.IsNull(MonsterTargetSelector.Infer([C("dead", 5, 0), C("far", 200, 1.0)], Now));
    }

    [TestMethod]
    public void Infer_KeepsAFarMonsterThatIsAlreadyHurt()
    {
        Assert.AreEqual("hurt", MonsterTargetSelector.Infer([C("hurt", 200, 0.5)], Now));
    }

    [TestMethod]
    public void Score_HalvesForUntouchedMonsters()
    {
        double full = MonsterTargetSelector.Score(C("a", 10, 1.0), Now);
        double nearlyFull = MonsterTargetSelector.Score(C("a", 10, 0.99), Now);
        Assert.IsTrue(full < nearlyFull * 0.6);
    }

    [TestMethod]
    public void Adapt_FollowsTheModeAndTheSwitch()
    {
        Assert.AreEqual(Target.None, MonsterTargetSelector.Adapt(false, TargetModeType.LockOn, Target.Self, Target.Self, Target.Self));
        Assert.AreEqual(Target.Self, MonsterTargetSelector.Adapt(true, TargetModeType.LockOn, Target.Self, Target.Another, Target.Another));
        Assert.AreEqual(Target.Another, MonsterTargetSelector.Adapt(true, TargetModeType.MapPin, Target.Self, Target.Another, Target.None));
        Assert.AreEqual(Target.None, MonsterTargetSelector.Adapt(true, TargetModeType.Infer, Target.Self, Target.Self, Target.None));
    }

    [TestMethod]
    public void Visible_ShowsOnlyTheTarget()
    {
        bool[] v = MonsterTargetSelector.Visible(true, true, [Target.Another, Target.Self, Target.None], [true, true, true]);
        CollectionAssert.AreEqual(new[] { false, true, false }, v);
    }

    [TestMethod]
    public void Visible_NoTarget_HidesAllOrShowsAllByTheOption()
    {
        Target[] none = [Target.None, Target.None];
        bool[] alive = [true, true];
        CollectionAssert.AreEqual(new[] { false, false }, MonsterTargetSelector.Visible(true, true, none, alive));
        CollectionAssert.AreEqual(new[] { true, true }, MonsterTargetSelector.Visible(true, false, none, alive));
    }

    [TestMethod]
    public void Visible_TargetingOff_ShowsEveryAliveMonster()
    {
        bool[] v = MonsterTargetSelector.Visible(false, true, [Target.None, Target.None], [true, false]);
        CollectionAssert.AreEqual(new[] { true, false }, v);
    }
}
