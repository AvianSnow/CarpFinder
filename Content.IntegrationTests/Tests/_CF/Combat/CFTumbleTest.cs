using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Tumble Through by running into an enemy, and how far a Leap goes.</summary>
[TestFixture]
[TestOf(typeof(CFManeuverSystem))]
public sealed class CFTumbleTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    private static readonly Vector2 East = new(1f, 0f);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFTumbleHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 200
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFTumbleHero
  id: CFTumbleFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFManeuverSystem Maneuvers => SEntMan.System<CFManeuverSystem>();
    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    private EntityUid Hero(int dexterity = Extreme)
    {
        var hero = SEntMan.SpawnEntity("CFTumbleHero", _map.Local(0, 0));
        SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Dexterity] = dexterity;
        return hero;
    }

    private EntityUid Beside(string id)
    {
        return SEntMan.SpawnEntity(id, _map.Local(0, 0).Offset(new Vector2(0.6f, 0f)));
    }

    private float Stamina(EntityUid mob)
    {
        return SEntMan.GetComponent<CFActionPoolComponent>(mob).Stamina;
    }

    [Test]
    public async Task OnlyAnEnemyInTheWayCounts()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            Beside("CFTumbleHero");
            Assert.That(Maneuvers.TryPushedEnemy(hero, East, out _), Is.False, "A friend is not tumbled through.");

            var foe = Beside("CFTumbleFoe");
            Assert.That(Maneuvers.TryPushedEnemy(hero, East, out var target));
            Assert.That(target, Is.EqualTo(foe));

            Assert.That(Maneuvers.TryPushedEnemy(hero, -East, out _), Is.False, "Heading away is not pushing.");
            Assert.That(Maneuvers.TryPushedEnemy(hero, Vector2.Zero, out _), Is.False);
        });
    }

    [Test]
    public async Task ASuccessTurnsPushingOff()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Beside("CFTumbleFoe");

            Assert.That(Maneuvers.TryTumble(hero, foe));
            Assert.That(Stamina(hero), Is.EqualTo(2f));
            Assert.That(Maneuvers.IsTumblingThrough(hero));

            var push = new AttemptMobCollideEvent();
            SEntMan.EventBus.RaiseLocalEvent(hero, ref push);
            Assert.That(push.Cancelled, "It no longer pushes others.");

            var pushed = new AttemptMobTargetCollideEvent();
            SEntMan.EventBus.RaiseLocalEvent(hero, ref pushed);
            Assert.That(pushed.Cancelled, "Others no longer push it.");

            Assert.That(Conditions.IsOffGuardTo(foe, hero), Is.False, "Off-guard needs Tumble Behind.");

            Assert.That(Maneuvers.TryTumble(hero, foe), Is.False, "Retries wait.");
            Assert.That(Stamina(hero), Is.EqualTo(2f));
        });
    }

    [Test]
    public async Task TumbleBehindLeavesTheEnemyOffGuard()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Beside("CFTumbleFoe");
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).TumbleBehind = true;

            Assert.That(Maneuvers.TryTumble(hero, foe));
            Assert.That(Conditions.IsOffGuardTo(foe, hero));
        });
    }

    [Test]
    public async Task AFailureSpendsTheActionAndStaysPut()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(dexterity: -Extreme);
            var foe = Beside("CFTumbleFoe");

            Assert.That(Maneuvers.TryTumble(hero, foe), Is.False);
            Assert.That(Stamina(hero), Is.EqualTo(2f));
            Assert.That(Maneuvers.IsTumblingThrough(hero), Is.False);
        });
    }

    [TestCase(10, 0)]
    [TestCase(15, 2)]
    [TestCase(25, 2)]
    [TestCase(30, 3)]
    public void LeapDistanceFollowsSpeed(int speedFeet, int tiles)
    {
        Assert.That(CFActivitySystem.LeapTiles(speedFeet), Is.EqualTo(tiles));
    }
}
