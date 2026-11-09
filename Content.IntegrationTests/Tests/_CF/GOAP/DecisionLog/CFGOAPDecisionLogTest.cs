using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.DecisionLog;
using Content.Shared.CF.GOAP;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;

/// <summary>FOR DEBUGGING ONLY, and removed with the decision log. Facts by name, as plain math.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPDecisionLogSystem))]
public sealed class CFGOAPDecisionLogMathTest
{
    [Test]
    public void ChangedFactsAreWrittenByName()
    {
        var before = (ulong) (CFGOAPFact.KnowsLkp | CFGOAPFact.HasWeapon);
        var after = (ulong) (CFGOAPFact.TargetVisible | CFGOAPFact.HasWeapon);
        var changes = CFGOAPDecisionLogSystem.FactChanges(before, after);

        Assert.That(changes, Does.Contain("+TargetVisible"));
        Assert.That(changes, Does.Contain("-KnowsLkp"));
        Assert.That(changes, Does.Not.Contain("HasWeapon"));
    }

    [Test]
    public void ActionsComingAndGoingAreLeftOut()
    {
        Assert.That(CFGOAPDecisionLogSystem.FactChanges(0, (ulong) CFGOAPFact.ActionsLeft), Is.Empty);
    }
}

/// <summary>FOR DEBUGGING ONLY, and removed with the decision log. A real brain writes down what it decided.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPDecisionLogSystem))]
public sealed class CFGOAPDecisionLogTest : CFGOAPLoggedTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task ItWritesDownWhatItDecided()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var written = string.Empty;
        await Server.WaitPost(() =>
        {
            var goap = SEntMan.System<CFGOAPSystem>();
            var uid = SEntMan.SpawnEntity("CFGOAPTestBoss", map.Local(0, 0));
            var brain = SEntMan.GetComponent<CFGOAPComponent>(uid);
            SEntMan.SpawnEntity("CFGOAPTestHero", map.Local(4, 0));

            goap.Start((uid, brain));
            goap.Think((uid, brain));
            SEntMan.System<CFGOAPDecisionLogSystem>().WatchAll();

            written = string.Join('\n', SEntMan.GetComponent<CFGOAPDecisionLogComponent>(uid).Entries);
        });

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("goal "));
            Assert.That(written, Does.Contain("+TargetVisible"));
            Assert.That(written, Does.Contain("target "));
        });
    }
}
