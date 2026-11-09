using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Shields in the hand, right-click maneuvers, Grapple by pulling, Escape, marks, and prone.</summary>
[TestFixture]
[TestOf(typeof(CFManeuverSystem))]
public sealed class CFManeuverTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFManeuverHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 200
  - type: CFAbilityScore
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFManeuverHero
  id: CFManeuverFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFManeuverSystem Maneuvers => SEntMan.System<CFManeuverSystem>();
    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();
    private CFShieldSystem Shields => SEntMan.System<CFShieldSystem>();
    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private EntityUid Mob(string id, int x, int strength = 0)
    {
        var mob = SEntMan.SpawnEntity(id, _map.Local(x, 0));
        SEntMan.GetComponent<CFAbilityScoreComponent>(mob).AbilityModifiers[CFAbility.Strength] = strength;
        return mob;
    }

    private void Rest(EntityUid mob)
    {
        if (SEntMan.TryGetComponent(mob, out CFActionPoolComponent? pool))
            pool.Stamina = 100f;
    }

    private EntityUid Give(EntityUid mob, string item)
    {
        var uid = SEntMan.SpawnEntity(item, _map.Local(0, 0));
        Assert.That(Hands.TryPickupAnyHand(mob, uid, checkActionBlocker: false));
        return uid;
    }

    [Test]
    public async Task ShieldStaysRaisedInEitherHandAndLowersWhenDropped()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Mob("CFManeuverHero", 0);
            var shield = Give(hero, "CFSteelShield");
            var comp = SEntMan.GetComponent<CFShieldComponent>(shield);

            Assert.That(Shields.RaisedBonus(hero), Is.Zero);
            Shields.SetRaised((shield, comp), true);
            Assert.That(Shields.RaisedBonus(hero), Is.EqualTo(2));

            Give(hero, "CFHoodedLantern");
            Assert.That(Shields.RaisedBonus(hero), Is.EqualTo(2), "It counts in either hand.");

            Assert.That(Hands.TryDrop(hero, shield, checkActionBlocker: false));
            Assert.That(comp.Raised, Is.False);
            Assert.That(Shields.RaisedBonus(hero), Is.Zero);
        });
    }

    [Test]
    public async Task RightClickChoosesTheManeuver()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Mob("CFManeuverHero", 0);
            var foe = Mob("CFManeuverFoe", 1);

            Assert.That(Maneuvers.Decide(hero, foe), Is.EqualTo(CFManeuver.Shove), "An empty-handed target can only be shoved.");

            Give(foe, "CFHoodedLantern");
            Assert.That(Maneuvers.Decide(hero, foe), Is.EqualTo(CFManeuver.Disarm));

            Give(hero, "CFSteelShield");
            Assert.That(Maneuvers.Decide(hero, foe), Is.EqualTo(CFManeuver.Shove), "A shield always shoves.");
        });
    }

    [Test]
    public async Task ThreeShovesKnockProne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var foe = Mob("CFManeuverFoe", 1);
            for (var i = 0; i < 2; i++)
            {
                Conditions.NoteShove(foe);
                Assert.That(Conditions.IsProne(foe), Is.False);
            }

            Conditions.NoteShove(foe);
            Assert.That(Conditions.IsProne(foe));
        });
    }

    [Test]
    public async Task CriticalFailureOnAShoveDropsTheShover()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Mob("CFManeuverHero", 0, strength: -Extreme);
            var foe = Mob("CFManeuverFoe", 1);

            // A natural 20 lifts this to a plain failure, so allow a few tries.
            for (var i = 0; i < 20 && !Conditions.IsProne(hero); i++)
            {
                Rest(hero);
                Maneuvers.TryManeuver(hero, foe);
            }

            Assert.That(Conditions.IsProne(hero));
            Assert.That(Conditions.IsProne(foe), Is.False);
        });
    }

    [Test]
    public async Task CriticalDisarmDropsTheItem()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Mob("CFManeuverHero", 0, strength: Extreme);
            var foe = Mob("CFManeuverFoe", 1);
            var item = Give(foe, "CFHoodedLantern");

            // A natural 1 lowers this to a plain success, so allow a few tries.
            for (var i = 0; i < 20 && Hands.IsHolding(foe, item); i++)
            {
                Rest(hero);
                Assert.That(Maneuvers.TryManeuver(hero, foe), Is.EqualTo(CFManeuver.Disarm));
            }

            Assert.That(Hands.IsHolding(foe, item), Is.False);
        });
    }

    [Test]
    public async Task PullingAnEnemyGrapplesAndAFriendIsFree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Mob("CFManeuverHero", 0, strength: Extreme);
            var foe = Mob("CFManeuverFoe", 1, strength: -Extreme);
            var friend = Mob("CFManeuverHero", 2);
            var pulling = SEntMan.System<PullingSystem>();

            Assert.That(pulling.TryStartPull(hero, foe));
            var cond = SEntMan.GetComponent<CFConditionComponent>(foe);
            Assert.That(cond.Grabbed);
            Assert.That(cond.Grabber, Is.EqualTo(hero));

            var pullable = SEntMan.GetComponent<PullableComponent>(foe);
            Assert.That(pulling.TryStopPull(foe, pullable, foe), Is.False, "A grabbed creature cannot walk out of the pull.");

            Assert.That(pulling.TryStopPull(foe, pullable, hero));
            Assert.That(cond.Grabbed, Is.False, "Letting go ends the grab.");

            var other = Mob("CFManeuverHero", 3, strength: Extreme);
            Assert.That(pulling.TryStartPull(other, friend));
            Assert.That(SEntMan.HasComponent<CFConditionComponent>(friend) && SEntMan.GetComponent<CFConditionComponent>(friend).Grabbed, Is.False);
        });
    }

    [Test]
    public async Task EscapeBreaksTheGrabAndThePull()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Mob("CFManeuverHero", 0, strength: -Extreme);
            var foe = Mob("CFManeuverFoe", 1, strength: Extreme);
            Conditions.ApplyGrabbed(foe, hero);
            var cond = SEntMan.GetComponent<CFConditionComponent>(foe);

            Assert.That(Maneuvers.TryEscape((foe, cond)));
            Assert.That(cond.Grabbed, Is.False);
        });
    }

    [Test]
    public async Task PointingMarksALivingEnemyOnce()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Mob("CFManeuverHero", 0);
            var foe = Mob("CFManeuverFoe", 3);
            var friend = Mob("CFManeuverHero", 1);
            var opt = SEntMan.EnsureComponent<CFCombatOptionsComponent>(hero);

            Assert.That(Maneuvers.TryMark((hero, opt), foe), Is.False, "Only marking classes mark.");

            opt.PointMark = CFPointMark.Prey;
            Assert.That(Maneuvers.TryMark((hero, opt), friend), Is.False);
            Assert.That(Maneuvers.TryMark((hero, opt), foe));
            Assert.That(opt.Prey, Is.EqualTo(foe));
            Assert.That(Maneuvers.TryMark((hero, opt), foe), Is.False, "Re-pointing at the mark costs nothing.");
        });
    }

    [Test]
    public async Task ProneMeansOffGuardAndWorseAttacks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Mob("CFManeuverHero", 0);
            Assert.That(Conditions.ProneAttackPenalty(hero), Is.Zero);

            Conditions.KnockProne(hero);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(hero));
            Assert.That(Conditions.ProneAttackPenalty(hero), Is.EqualTo(-2));

            SEntMan.System<CFBonusSystem>().AddBonus(hero, CFBonusStat.AttackRoll, CFBonusType.Circumstance, -2, 6f);
            Assert.That(Conditions.ProneAttackPenalty(hero), Is.Zero, "Circumstance penalties do not stack.");
        });
    }

    [Test]
    public async Task StandingCostsOneActionAndWaitsForIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid rested = default;
        EntityUid spent = default;
        await Server.WaitAssertion(() =>
        {
            rested = ProneWith(0, 3f);
            spent = ProneWith(4, 0f);
        });

        // KnockProne locks a creature down for 3 seconds, and stock takes 2 more to get up.
        await Pair.RunSeconds(7);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Conditions.IsProne(rested), Is.False);
            Assert.That(SEntMan.GetComponent<CFActionPoolComponent>(rested).Stamina, Is.EqualTo(2f));
            Assert.That(Conditions.IsProne(spent), "With no action left, the stand waits.");
        });
    }

    [Test]
    public async Task DeletingAProneCreatureLogsNoError()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Mob("CFManeuverHero", 0);
            Conditions.KnockProne(hero);
            SEntMan.DeleteEntity(hero);
        });
    }

    private EntityUid ProneWith(int x, float stamina)
    {
        var mob = Mob("CFManeuverHero", x);
        var pool = SEntMan.EnsureComponent<CFActionPoolComponent>(mob);
        pool.Stamina = stamina;
        pool.NpcRegenSeconds = 1000f;
        Conditions.KnockProne(mob);
        return mob;
    }
}
