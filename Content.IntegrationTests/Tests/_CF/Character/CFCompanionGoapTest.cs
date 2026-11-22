using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Companions;
using Content.Server.CF.GOAP;
using Content.Shared.CF.Companions;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>
/// Companions think with GOAP: they go for enemies by themselves, pointing tells them which one first, and their home
/// is their master.
/// </summary>
[TestFixture]
[TestOf(typeof(CFCompanionSystem))]
public sealed class CFCompanionGoapTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 5, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCompanionGoapTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFCompanionGoapTestHero
  id: CFCompanionGoapTestFoe
  components:
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFCompanionGoapTestHero
  id: CFCompanionGoapTestBystander
  components:
  - type: CFFaction
    faction: Neutral
";

    private CFDevMap _map = default!;

    private EntityUid Spawn(string id, int x, int y = 0) => SEntMan.SpawnEntity(id, _map.Local(x, y));

    private EntityUid? BrainTarget(EntityUid companion) => SEntMan.GetComponent<CFGOAPComponent>(companion).Target;

    private EntityUid Companion(EntityUid hero, int x)
    {
        var companion = Spawn("CFSummon", x);
        SEntMan.System<CFCompanionSystem>().Bind(hero, companion);
        return companion;
    }

    [Test]
    public async Task ItGoesForAMonsterByItselfAndLeavesBystandersAlone()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid companion = default, foe = default;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFCompanionGoapTestHero", 0);
            companion = Companion(hero, 1);
            Spawn("CFCompanionGoapTestBystander", 2);
            foe = Spawn("CFCompanionGoapTestFoe", 4);
        });

        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CFGOAPComponent>(companion), "Every companion has a brain.");
            Assert.That(BrainTarget(companion), Is.EqualTo(foe), "The monster, not the closer bystander.");
        });
    }

    [Test]
    public async Task PointingSaysWhichEnemyFirst()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid companion = default, far = default;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFCompanionGoapTestHero", 0);
            companion = Companion(hero, 1);
            Spawn("CFCompanionGoapTestFoe", 2);
            far = Spawn("CFCompanionGoapTestFoe", 4);
            SEntMan.System<CFCompanionSystem>().OrderAttack(companion, far);
        });

        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
            Assert.That(BrainTarget(companion), Is.EqualTo(far), "The one pointed at, though another is closer."));
    }

    [Test]
    public async Task ItsHomeIsItsMaster()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, companion = default;
        await Server.WaitPost(() =>
        {
            hero = Spawn("CFCompanionGoapTestHero", 0);
            companion = Companion(hero, 1);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitPost(() =>
            SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, _map.Local(4, 1)));
        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var transform = SEntMan.System<SharedTransformSystem>();
            Assert.That(SEntMan.System<CFGOAPSystem>().TryGetHome((companion, SEntMan.GetComponent<CFGOAPComponent>(companion)), out var home));
            var master = transform.GetMapCoordinates(hero).Position;
            Assert.That(Vector2.Distance(home.Position, master), Is.LessThan(0.5f), "Home moved with the master.");
        });
    }
}
