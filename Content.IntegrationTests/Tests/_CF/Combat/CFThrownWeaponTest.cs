using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.Hands.Systems;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.Components;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Throwing a weapon with the thrown trait is a ranged Strike that flies in a line.</summary>
[TestFixture]
[TestOf(typeof(CFStrikeSystem))]
public sealed class CFThrownWeaponTest : GameTest
{
    private const int Extreme = 60;

    private const int FoeHp = 100000;

    /// <summary>The flight stretch in <see cref="CFStrikeSystem"/>.</summary>
    private const float Stretch = 1.2f;

    private static readonly Box2i Footprint = new(-1, -1, 11, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFThrowHero
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
  id: CFThrowFoe
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

    private HandsSystem Hands => SEntMan.System<HandsSystem>();

    private EntityUid Hero(int dexterity = Extreme)
    {
        var hero = SEntMan.SpawnEntity("CFThrowHero", _map.Local(0, 0));
        SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Dexterity] = dexterity;

        // Nothing refills, so the pool and MAP can be read after the flight.
        Pool(hero).RegenSeconds = 100000f;
        return hero;
    }

    private EntityUid Foe(int x)
    {
        return SEntMan.SpawnEntity("CFThrowFoe", _map.Local(x, 0));
    }

    private CFActionPoolComponent Pool(EntityUid mob)
    {
        return SEntMan.GetComponent<CFActionPoolComponent>(mob);
    }

    private int Hp(EntityUid mob)
    {
        return SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;
    }

    private EntityUid Give(EntityUid mob, string item)
    {
        var uid = SEntMan.SpawnEntity(item, _map.Local(0, 0));
        Assert.That(Hands.TryPickupAnyHand(mob, uid, checkActionBlocker: false));
        return uid;
    }

    private void Throw(EntityUid hero, int x)
    {
        Assert.That(Hands.ThrowHeldItem(hero, _map.Local(x, 0)));
    }

    private double FlightSeconds(EntityUid item)
    {
        var thrown = SEntMan.GetComponent<ThrownItemComponent>(item);
        return (thrown.LandTime!.Value - thrown.ThrownTime!.Value).TotalSeconds;
    }

    [Test]
    public async Task RangeComesFromTheRangeIncrement()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.System<CFWeaponSystem>();
            Assert.That(weapons.ThrowRange(SEntMan.SpawnEntity("CFSpear", _map.Local(0, 0)), 8f), Is.EqualTo(24f), "Six increments of 20 feet.");
            Assert.That(weapons.ThrowRange(SEntMan.SpawnEntity("CFDagger", _map.Local(0, 0)), 8f), Is.EqualTo(12f), "Six increments of 10 feet.");
            Assert.That(weapons.ThrowRange(SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0)), 8f), Is.EqualTo(8f), "No thrown trait.");
        });
    }

    [Test]
    public async Task AFarThrowStopsAtTheLastRangeIncrement()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var speed = SEntMan.GetComponent<HandsComponent>(hero).BaseThrowspeed;
            var spear = Give(hero, "CFSpear");
            Throw(hero, 40);
            Assert.That(FlightSeconds(spear), Is.EqualTo(24f / speed * Stretch).Within(0.05));

            var other = Hero();
            var dagger = Give(other, "CFDagger");
            Throw(other, 40);
            Assert.That(FlightSeconds(dagger), Is.EqualTo(12f / speed * Stretch).Within(0.05));
        });
    }

    [Test]
    public async Task ASpearFliesPointFirst()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var spear = Give(hero, "CFSpear");
            Throw(hero, 6);

            Assert.That(SEntMan.GetComponent<PhysicsComponent>(spear).AngularVelocity, Is.EqualTo(0f), "It does not spin.");

            // The sprite's tip points east, so a throw east leaves it as drawn.
            var rotation = SEntMan.GetComponent<TransformComponent>(spear).LocalRotation;
            Assert.That(rotation.EqualsApprox(Angle.Zero, 0.01), $"Rotated {rotation.Degrees} degrees.");
        });
    }

    [Test]
    public async Task AHitDamagesAndSticks([Values(2, 6, 9)] int distance)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, foe = default, spear = default;
        await Server.WaitAssertion(() =>
        {
            hero = Hero();
            foe = Foe(distance);
            spear = Give(hero, "CFSpear");
            Throw(hero, distance);
            Assert.That(Pool(hero).Stamina, Is.EqualTo(2f), "It costs one action as it leaves the hand.");
        });

        await Pair.RunSeconds(1.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Hp(foe), Is.LessThan(FoeHp));
            Assert.That(SEntMan.GetComponent<EmbeddableProjectileComponent>(spear).EmbeddedIntoUid, Is.EqualTo(foe));
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(spear), Is.False);
            Assert.That(Pool(hero).MapAttacks, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AnyThrownWeaponReachesTheCursor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid foe = default;
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            foe = Foe(6);
            Give(hero, "CFDagger");
            Throw(hero, 6);
        });

        await Pair.RunSeconds(1.5f);
        await Server.WaitAssertion(() => Assert.That(Hp(foe), Is.LessThan(FoeHp)));
    }

    [Test]
    public async Task AMissFallsPastAndHurtsNobodyElse()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, first = default, second = default, spear = default;
        await Server.WaitAssertion(() =>
        {
            hero = Hero(dexterity: -Extreme);
            first = Foe(4);
            second = Foe(5);
            spear = Give(hero, "CFSpear");
            Throw(hero, 9);
        });

        await Pair.RunSeconds(1.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Hp(first), Is.EqualTo(FoeHp));
            Assert.That(Hp(second), Is.EqualTo(FoeHp));
            Assert.That(SEntMan.GetComponent<EmbeddableProjectileComponent>(spear).EmbeddedIntoUid, Is.Null);
            Assert.That(Pool(hero).MapAttacks, Is.EqualTo(1), "One throw is one Strike.");

            var transform = SEntMan.System<SharedTransformSystem>();
            var flown = Vector2.Distance(transform.GetWorldPosition(hero), transform.GetWorldPosition(spear));
            Assert.That(flown, Is.GreaterThan(3f).And.LessThan(7f), "It ends near the creature it missed.");
        });
    }

    [Test]
    public async Task ItFliesPastAnAlly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, ally = default, foe = default, spear = default;
        await Server.WaitAssertion(() =>
        {
            hero = Hero();
            ally = SEntMan.SpawnEntity("CFThrowHero", _map.Local(3, 0));
            foe = Foe(6);
            spear = Give(hero, "CFSpear");
            Throw(hero, 6);
        });

        await Pair.RunSeconds(1.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Hp(ally), Is.EqualTo(200));
            Assert.That(SEntMan.GetComponent<EmbeddableProjectileComponent>(spear).EmbeddedIntoUid, Is.EqualTo(foe));
            Assert.That(Pool(hero).MapAttacks, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task WithNoActionsItStaysInHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            Pool(hero).Stamina = 0f;
            var spear = Give(hero, "CFSpear");
            Throw(hero, 6);

            Assert.That(Hands.IsHolding(hero, spear));
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(spear), Is.False);
        });
    }

    [Test]
    public async Task TossingAnythingElseIsFree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var sword = Give(hero, "CFLongsword");
            Throw(hero, 6);

            Assert.That(Hands.IsHolding(hero, sword), Is.False);
            Assert.That(Pool(hero).Stamina, Is.EqualTo(3f));
        });
    }

    /// <summary>Without it stock ends the flight short of the cursor, so the weapon cannot hit what it is aimed at.</summary>
    [Test]
    public async Task EveryThrownWeaponLandsAtTheCursor()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            var prototypes = Server.ProtoMan;
            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.TryComp<CFWeaponComponent>(out _, factory) || !proto.TryComp<CFTraitComponent>(out var traits, factory))
                    continue;

                if (CFWeaponTraits.From(prototypes, traits.Traits).Thrown > 0)
                    Assert.That(proto.TryComp<LandAtCursorComponent>(out _, factory), $"{proto.ID} has the thrown trait but no LandAtCursor.");
            }
        });
    }
}
