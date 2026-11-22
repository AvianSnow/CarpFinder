using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Feats that moved off the wheel onto the normal attack inputs.</summary>
[TestFixture]
[TestOf(typeof(CFStrikeSystem))]
public sealed class CFFeatSwingTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    private static readonly Vector2 Wide = new(1f, 0f);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSwingHero
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
  parent: MobHuman
  id: CFSwingFoe
  components:
  - type: CFCombatant
    level: 1
    armorClass: 20
    maxHp: 100000
    currentHp: 100000
  - type: CFAbilityScore
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();
    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private EntityUid Hero(int strength = Extreme)
    {
        var hero = SEntMan.SpawnEntity("CFSwingHero", _map.Local(0, 0));
        SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Strength] = strength;
        return hero;
    }

    private EntityUid Foe(int x = 1)
    {
        return SEntMan.SpawnEntity("CFSwingFoe", _map.Local(x, 0));
    }

    private CFCombatOptionsComponent Options(EntityUid mob)
    {
        return SEntMan.GetComponent<CFCombatOptionsComponent>(mob);
    }

    private CFActionPoolComponent Pool(EntityUid mob)
    {
        return SEntMan.GetComponent<CFActionPoolComponent>(mob);
    }

    private EntityUid Give(EntityUid mob, string item)
    {
        var uid = SEntMan.SpawnEntity(item, _map.Local(0, 0));
        Assert.That(Hands.TryPickupAnyHand(mob, uid, checkActionBlocker: false));
        return uid;
    }

    /// <summary>Stock raises this from both attacks. A click has no direction.</summary>
    private void Swing(EntityUid user, EntityUid weapon, Vector2? direction, params EntityUid[] targets)
    {
        var hit = new MeleeHitEvent(new HashSet<EntityUid>(targets), user, weapon, new DamageSpecifier(), direction);
        SEntMan.EventBus.RaiseLocalEvent(weapon, hit);
    }

    private void NewTurn(EntityUid mob)
    {
        var pool = Pool(mob);
        pool.Stamina = 3f;
        pool.MapAttacks = 0;
        pool.Flourished = false;
    }

    [Test]
    public async Task ViciousSwingNeedsAWideSwingAtOneCreature()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var sword = Give(hero, "CFLongsword");
            var foe = Foe();
            var other = Foe(2);
            Options(hero).ViciousSwing = true;
            var pool = Pool(hero);

            Swing(hero, sword, null, foe);
            Assert.That(pool.MapAttacks, Is.EqualTo(1), "A click is a plain Strike.");
            Assert.That(pool.Flourished, Is.False);
            Assert.That(pool.Stamina, Is.EqualTo(3f));

            NewTurn(hero);
            Swing(hero, sword, Wide, foe);
            Assert.That(pool.MapAttacks, Is.EqualTo(2), "It counts as two attacks.");
            Assert.That(pool.Flourished);
            Assert.That(pool.Stamina, Is.EqualTo(2f), "The swing pays the other action.");

            Swing(hero, sword, Wide, foe);
            Assert.That(pool.MapAttacks, Is.EqualTo(3), "A second flourish is a normal swing.");
            Assert.That(pool.Stamina, Is.EqualTo(2f));

            NewTurn(hero);
            Swing(hero, sword, Wide, foe, other);
            Assert.That(pool.MapAttacks, Is.EqualTo(1), "Two creatures stay a wide swing.");
            Assert.That(pool.Flourished, Is.False);

            NewTurn(hero);
            pool.Stamina = 1f;
            Swing(hero, sword, Wide, foe);
            Assert.That(pool.MapAttacks, Is.EqualTo(1), "One action cannot pay for it.");
            Assert.That(pool.Stamina, Is.EqualTo(1f));
        });
    }

    [Test]
    public async Task DoubleSliceStrikesWithEachWeapon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var sword = Give(hero, "CFLongsword");
            var foe = Foe();
            var other = Foe(2);
            Options(hero).DoubleSlice = true;
            var pool = Pool(hero);

            Swing(hero, sword, Wide, foe);
            Assert.That(pool.MapAttacks, Is.EqualTo(1), "One weapon is a normal swing.");
            Assert.That(pool.Stamina, Is.EqualTo(3f));

            Give(hero, "CFDagger");
            NewTurn(hero);
            Swing(hero, sword, Wide, foe);
            Assert.That(pool.MapAttacks, Is.EqualTo(2), "It counts as two attacks.");
            Assert.That(pool.Stamina, Is.EqualTo(2f));

            NewTurn(hero);
            Swing(hero, sword, Wide, foe, other);
            Assert.That(pool.MapAttacks, Is.EqualTo(1), "Two creatures stay a wide swing.");

            NewTurn(hero);
            Swing(hero, sword, null, foe);
            Assert.That(pool.MapAttacks, Is.EqualTo(1), "A click stays one Strike.");
        });
    }

    [Test]
    public async Task TwinFeintFindsTheTargetOffGuard()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var sword = Give(hero, "CFLongsword");
            Give(hero, "CFDagger");
            var foe = Foe();
            Options(hero).TwinFeint = true;

            Swing(hero, sword, Wide, foe);
            Assert.That(Pool(hero).MapAttacks, Is.EqualTo(2));
            Assert.That(Conditions.IsOffGuardTo(foe, hero));
        });
    }

    [Test]
    public async Task SnaggingStrikeNeedsAFreeHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var sword = Give(hero, "CFLongsword");
            Options(hero).SnaggingStrike = true;

            var foe = Foe();
            Swing(hero, sword, null, foe);
            Assert.That(Conditions.IsOffGuardTo(foe, hero));

            Give(hero, "CFHoodedLantern");
            var held = Foe(2);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(held, _map.Local(1, 0));
            Swing(hero, sword, null, held);
            Assert.That(Conditions.IsOffGuardTo(held, hero), Is.False);
        });
    }

    [Test]
    public async Task ExactingStrikeRefundsAPlainMissWithAPenalty()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            // AC 20 against a plain attacker: a mix of plain misses and critical misses.
            var hero = Hero(strength: 0);
            var sword = Give(hero, "CFLongsword");
            var foe = Foe();
            var pool = Pool(hero);
            var seen = new HashSet<int>();

            for (var i = 0; i < 100; i++)
            {
                pool.MapAttacks = 1;
                Swing(hero, sword, null, foe);
                seen.Add(pool.MapAttacks);
            }

            Assert.That(seen, Is.EquivalentTo(new[] { 2 }), "Without the feat every Strike counts.");

            Options(hero).ExactingStrike = true;
            seen.Clear();
            for (var i = 0; i < 100; i++)
            {
                pool.MapAttacks = 1;
                Swing(hero, sword, null, foe);
                seen.Add(pool.MapAttacks);
            }

            Assert.That(seen, Is.EquivalentTo(new[] { 1, 2 }), "A plain miss refunds. A critical miss does not.");

            seen.Clear();
            for (var i = 0; i < 100; i++)
            {
                pool.MapAttacks = 0;
                Swing(hero, sword, null, foe);
                seen.Add(pool.MapAttacks);
            }

            Assert.That(seen, Is.EquivalentTo(new[] { 1 }), "Press: it needs a multiple attack penalty.");
        });
    }
}
