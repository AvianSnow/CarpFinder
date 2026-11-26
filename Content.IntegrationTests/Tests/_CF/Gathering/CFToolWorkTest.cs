using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Gathering;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Gathering;

/// <summary>
/// Axes fell trees and picks mine rock in a fixed number of swings with no roll, and what breaks drops what it should.
/// </summary>
[TestFixture]
[TestOf(typeof(CFToolWorkSystem))]
public sealed class CFToolWorkTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFToolWorkTestHero
  components:
  - type: CFCombatant
    thinkingTimer: 0
    level: 5
    armorClass: 12
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFActionPool
";

    private CFDevMap _map = default!;

    private CFToolWorkSystem Work => SEntMan.System<CFToolWorkSystem>();

    private int Count(string prototype)
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == prototype && !SEntMan.IsQueuedForDeletion(uid))
                count++;
        }

        return count;
    }

    private bool Gone(EntityUid uid) => SEntMan.Deleted(uid) || SEntMan.IsQueuedForDeletion(uid);

    [Test]
    public async Task AnAxeFellsATreeInThreeSwings()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFToolWorkTestHero", _map.Local(0, 0));
            var axe = SEntMan.SpawnEntity("CFHatchet", _map.Local(0, 0));
            var pick = SEntMan.SpawnEntity("CFPick", _map.Local(0, 0));
            var tree = SEntMan.SpawnEntity("CFFloraTreeGreen", _map.Local(1, 0));
            var logs = Count("CFWoodLog");

            Assert.That(Work.TryWork(hero, pick, tree), Is.False, "A pick does not chop.");
            Assert.That(Work.TryWork(hero, hero, tree), Is.False, "Nor do fists.");

            Assert.That(Work.TryWork(hero, axe, tree), Is.True);
            Assert.That(Work.TryWork(hero, axe, tree), Is.True);
            Assert.That(Gone(tree), Is.False, "Two swings are not enough.");
            Assert.That(Count("CFWoodLog"), Is.EqualTo(logs));

            Assert.That(Work.TryWork(hero, axe, tree), Is.True);
            Assert.That(Gone(tree), Is.True, "The third swing fells it.");
            Assert.That(Count("CFWoodLog"), Is.GreaterThan(logs), "A felled tree drops logs.");
        });
    }

    [Test]
    public async Task APickMinesAnOreVein()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFToolWorkTestHero", _map.Local(0, 0));
            var axe = SEntMan.SpawnEntity("CFBattleAxe", _map.Local(0, 0));
            var pick = SEntMan.SpawnEntity("CFPick", _map.Local(0, 0));
            var vein = SEntMan.SpawnEntity("CFWallStoneCopperOre", _map.Local(1, 0));
            var ore = Count("CFOreCopper1");

            Assert.That(Work.TryWork(hero, axe, vein), Is.False, "An axe does not mine.");

            for (var i = 0; i < 3; i++)
            {
                Assert.That(Work.TryWork(hero, pick, vein), Is.True);
            }

            Assert.That(Gone(vein), Is.False);
            Assert.That(Work.TryWork(hero, pick, vein), Is.True);
            Assert.That(Gone(vein), Is.True, "The fourth swing breaks it.");
            Assert.That(Count("CFOreCopper1"), Is.GreaterThan(ore), "A mined vein drops its ore.");
        });
    }

    /// <summary>
    /// The bug this guards: an object beaten down by Strikes took one huge hit, which fired the overkill threshold
    /// listed first, and that one drops nothing.
    /// </summary>
    [Test]
    public async Task ATreeBeatenDownByStrikesStillDropsLogs()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFToolWorkTestHero", _map.Local(0, 0));
            var sword = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
            var tree = SEntMan.SpawnEntity("CFFloraTreeGreen", _map.Local(1, 0));
            var logs = Count("CFWoodLog");

            Assert.That(SEntMan.System<CFWeaponSystem>().TryStats(sword, out var stats));
            var strikes = SEntMan.System<CFStrikeSystem>();
            for (var i = 0; i < 20 && !Gone(tree); i++)
            {
                strikes.PerformStrike(hero, tree, stats, sword, melee: true, countMap: false, fixedMap: 0);
            }

            Assert.That(Gone(tree), Is.True);
            Assert.That(Count("CFWoodLog"), Is.GreaterThan(logs));
        });
    }

    [Test]
    public async Task AnAxeSplitsALogIntoPlanks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFToolWorkTestHero", _map.Local(0, 0));
            var axe = SEntMan.SpawnEntity("CFHatchet", _map.Local(0, 0));
            var log = SEntMan.SpawnEntity("CFWoodLog", _map.Local(1, 0));
            var planks = Count("CFWoodenPlanks1");

            var ev = new InteractUsingEvent(hero, axe, log, _map.Local(1, 0));
            SEntMan.EventBus.RaiseLocalEvent(log, ev);

            Assert.That(ev.Handled, Is.True);
            Assert.That(Gone(log), Is.True);
            Assert.That(Count("CFWoodenPlanks1"), Is.GreaterThan(planks));
        });
    }
}
