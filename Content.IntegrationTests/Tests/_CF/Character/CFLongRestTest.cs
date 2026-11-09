using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Falling asleep in a bed restores a character. Falling asleep anywhere else does not.</summary>
[TestFixture]
[TestOf(typeof(CFLongRestSystem))]
public sealed class CFLongRestTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFRestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 20
    currentHp: 5
    wounded: 2
  - type: CFAbilityScore
  - type: CFSpellbook
    slots: [ 5, 2, 1 ]
    expended: [ 0, 2, 1 ]
    focusPoints: 1
    focusSpent: 1
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: MobHuman
  id: CFRestFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 20
    currentHp: 20
  - type: CFAbilityScore
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private void AssertTired(EntityUid hero)
    {
        var book = SEntMan.GetComponent<CFSpellbookComponent>(hero);
        Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.EqualTo(5));
        Assert.That(book.Expended, Is.EqualTo(new List<int> { 0, 2, 1 }));
    }

    [Test]
    public async Task SleepingInABedIsALongRest()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFRestHero", _map.Local(0, 0));
            var bed = SEntMan.SpawnEntity("CFWoodenBed", _map.Local(0, 0));
            var sleeping = SEntMan.System<SleepingSystem>();

            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(hero, hero, bed));
            AssertTired(hero);

            Assert.That(sleeping.TrySleeping(hero));

            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var book = SEntMan.GetComponent<CFSpellbookComponent>(hero);
            Assert.Multiple(() =>
            {
                Assert.That(combat.CurrentHp, Is.EqualTo(20));
                Assert.That(combat.Wounded, Is.Zero);
                Assert.That(book.Expended, Is.EqualTo(new List<int> { 0, 0, 0 }));
                Assert.That(book.FocusSpent, Is.Zero);
                Assert.That(SEntMan.HasComponent<SleepingComponent>(hero), "A rest does not wake them.");
            });
        });
    }

    private EntityUid HeroInBed()
    {
        var hero = SEntMan.SpawnEntity("CFRestHero", _map.Local(0, 0));
        var bed = SEntMan.SpawnEntity("CFWoodenBed", _map.Local(0, 0));
        Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(hero, hero, bed));
        return hero;
    }

    [Test]
    public async Task AnEnemyCloseByStopsTheRestUntilItIsDead()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = HeroInBed();
            var foe = SEntMan.SpawnEntity("CFRestFoe", _map.Local(1, 0));
            var sleeping = SEntMan.System<SleepingSystem>();

            Assert.That(sleeping.TrySleeping(hero), Is.False);
            AssertTired(hero);
            Assert.That(SEntMan.HasComponent<SleepingComponent>(hero), Is.False);

            SEntMan.System<MobStateSystem>().ChangeMobState(foe, MobState.Dead);

            Assert.That(sleeping.TrySleeping(hero));
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.EqualTo(20));
        });
    }

    [Test]
    public async Task AnEnemyDoesNotStopSleepingOnTheFloor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFRestHero", _map.Local(0, 0));
            SEntMan.SpawnEntity("CFRestFoe", _map.Local(1, 0));

            Assert.That(SEntMan.System<SleepingSystem>().TrySleeping(hero));
        });
    }

    [Test]
    public async Task ARestHasToWaitForItsCooldown()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = HeroInBed();
            var sleeping = SEntMan.System<SleepingSystem>();
            var rest = SEntMan.System<CFLongRestSystem>();
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);

            Assert.That(sleeping.TrySleeping(hero));
            Assert.That(rest.MinutesLeft(hero), Is.EqualTo(15));

            SEntMan.RemoveComponent<SleepingComponent>(hero);
            combat.CurrentHp = 5;

            Assert.That(sleeping.TrySleeping(hero), Is.False, "Too soon.");
            Assert.That(combat.CurrentHp, Is.EqualTo(5));

            SEntMan.GetComponent<CFRestCooldownComponent>(hero).ReadyAt = SGameTiming.CurTime;

            Assert.That(sleeping.TrySleeping(hero));
            Assert.That(combat.CurrentHp, Is.EqualTo(20));
        });
    }

    [Test]
    public async Task SleepingOnTheFloorRestoresNothing()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFRestHero", _map.Local(0, 0));

            Assert.That(SEntMan.System<SleepingSystem>().TrySleeping(hero));

            AssertTired(hero);
        });
    }

    [Test]
    public async Task ARestDoesNotReviveTheDying()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFRestHero", _map.Local(0, 0));
            var dying = SEntMan.System<CFDyingSystem>();
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            SEntMan.System<Content.Shared.CF.Effects.CFEffectSystem>().DealDamage(hero, hero, 1000, "Slashing", false);
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(hero), Is.False);

            dying.RestoreFully(hero);

            Assert.That(combat.CurrentHp, Is.Zero);
        });
    }
}
