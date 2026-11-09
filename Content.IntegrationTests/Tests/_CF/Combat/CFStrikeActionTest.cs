using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>How a two-handed grip changes a weapon. Plain math, so no game runs.</summary>
[TestFixture]
public sealed class CFTwoHandedGripTest
{
    private static CFStrikeWeaponStats Weapon(int die, int twoHand = 0)
    {
        return new CFStrikeWeaponStats(1, die, "Slashing", "MartialWeapons", new CFWeaponTraits { TwoHand = twoHand });
    }

    [TestCase(4, 0, ExpectedResult = 6, TestName = "A d4 goes up one step")]
    [TestCase(10, 0, ExpectedResult = 12, TestName = "A d10 goes up one step")]
    [TestCase(12, 0, ExpectedResult = 12, TestName = "A d12 stays")]
    [TestCase(8, 12, ExpectedResult = 12, TestName = "The two-hand trait uses its own die")]
    public int Die(int die, int twoHand)
    {
        return CFStrikeActionSystem.WithTwoHandedGrip(Weapon(die, twoHand)).DamageDie;
    }
}

/// <summary>Strike actions through the same attempt and perform steps the action bar uses. Strength 40 always hits, and critically hits unless the die shows a 1, so a test of a critical makes several attempts.</summary>
[TestFixture]
[TestOf(typeof(CFStrikeActionSystem))]
public sealed class CFStrikeActionTest : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFStrikeActionHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 1
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFStrikeActionHero
  id: CFStrikeActionFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private EntityUid Spawn(string id, int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private Entity<ActionComponent> Grant(EntityUid hero, string id)
    {
        SEntMan.System<CFCharacterFeatureSystem>().GrantAction(hero, id);
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool CanUse(EntityUid hero, EntityUid action, EntityUid target)
    {
        var attempt = new ActionAttemptEvent(hero, target);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        return !attempt.Cancelled;
    }

    private bool Use(EntityUid hero, Entity<ActionComponent> action, EntityUid target)
    {
        if (!CanUse(hero, action, target))
            return false;

        SEntMan.System<SharedActionsSystem>().PerformAction(hero, action, new CFActivityTargetEvent { Target = target });
        return true;
    }

    private int Hurt(EntityUid mob)
    {
        return StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;
    }

    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    private void Hold(EntityUid hero, string id)
    {
        SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, Spawn(id));
    }

    [Test]
    public async Task IntimidatingStrikeFrightensAndCostsTwoActions()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int lowest = 9, highest = 0, hurt = 0;
        float before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFStrikeActionHero");
            var action = Grant(hero, "CFActIntimidatingStrike");
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            for (var i = 0; i < 10; i++)
            {
                var foe = Spawn("CFStrikeActionFoe", 1);
                before = pool.Stamina;
                Use(hero, action, foe);
                after = pool.Stamina;
                lowest = Math.Min(lowest, Conditions.Frightened(foe));
                highest = Math.Max(highest, Conditions.Frightened(foe));
                hurt = Hurt(foe);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(hurt, Is.GreaterThan(0));
            Assert.That(lowest, Is.GreaterThanOrEqualTo(1), "Every hit frightens.");
            Assert.That(highest, Is.EqualTo(2), "A critical hit frightens more.");
            Assert.That(after, Is.EqualTo(before - 2f));
        });
    }

    [Test]
    public async Task HeadStompNeedsAProneTarget()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool standing = true, prone = true;
        var stupefied = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFStrikeActionHero");
            var action = Grant(hero, "CFActHeadStomp");
            standing = CanUse(hero, action, Spawn("CFStrikeActionFoe", 1));
            for (var i = 0; i < 10; i++)
            {
                var foe = Spawn("CFStrikeActionFoe", 1);
                Conditions.ApplyCondition(foe, CFConditionKind.Prone, 0, 6f, hero, 0);
                prone &= Use(hero, action, foe);
                stupefied = Math.Max(stupefied, Conditions.Stupefied(foe));
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(standing, Is.False);
            Assert.That(prone, Is.True);
            Assert.That(stupefied, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TwinTakedownNeedsPreyAndTwoWeaponsThenStrikesTwice()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool noWeapons = true, notPrey = true, used = false, secondFlourish = true;
        var attacks = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFStrikeActionHero");
            var foe = Spawn("CFStrikeActionFoe", 1);
            var other = Spawn("CFStrikeActionFoe", 1);
            var action = Grant(hero, "CFActTwinTakedown");
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).Prey = foe;
            noWeapons = CanUse(hero, action, foe);
            Hold(hero, "CFDagger");
            Hold(hero, "CFDagger");
            notPrey = CanUse(hero, action, other);
            used = Use(hero, action, foe);
            attacks = SEntMan.GetComponent<CFActionPoolComponent>(hero).MapAttacks;
            secondFlourish = CanUse(hero, action, foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(noWeapons, Is.False);
            Assert.That(notPrey, Is.False);
            Assert.That(used, Is.True);
            Assert.That(attacks, Is.EqualTo(2), "One Strike with each weapon.");
            Assert.That(secondFlourish, Is.False, "It is a flourish.");
        });
    }

    [Test]
    public async Task DualHandedAssaultNeedsAFreeHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool free = false, full = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFStrikeActionHero");
            var foe = Spawn("CFStrikeActionFoe", 1);
            var action = Grant(hero, "CFActDualHandedAssault");
            Hold(hero, "CFDagger");
            free = CanUse(hero, action, foe);
            Hold(hero, "CFDagger");
            full = CanUse(hero, action, foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(free, Is.True);
            Assert.That(full, Is.False);
        });
    }

    /// <summary>The ranged Skeleton Guard already holds a bow with an arrow on it, so it can use a real shot.</summary>
    [Test]
    public async Task HuntersAimSpendsTheArrowAndNeedsOne()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid archer = default, target = default, bow = default;
        Entity<ActionComponent> action = default;
        await Server.WaitPost(() =>
        {
            archer = Spawn("CFMobSkeletonguardRanged");
            target = Spawn("CFStrikeActionFoe", 3);
            SEntMan.GetComponent<CFArrowSupplyComponent>(archer).Arrows = 0;
            action = Grant(archer, "CFActHuntersAim");
        });

        await Pair.RunTicksSync(5);

        int before = 0, after = -1;
        bool notPrey = true, used = false, empty = true;
        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            bow = hands.EnumerateHeld(archer).First(held => SEntMan.HasComponent<CFWeaponComponent>(held));
            before = Arrows(bow);
            notPrey = CanUse(archer, action, target);
            SEntMan.GetComponent<CFCombatOptionsComponent>(archer).Prey = target;
            used = Use(archer, action, target);
            after = Arrows(bow);
            empty = CanUse(archer, action, target);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(1), "The bow starts nocked.");
            Assert.That(notPrey, Is.False);
            Assert.That(used, Is.True);
            Assert.That(after, Is.Zero);
            Assert.That(empty, Is.False, "It cannot fire with no arrow on the bow.");
        });
    }

    private int Arrows(EntityUid bow)
    {
        var count = new GetAmmoCountEvent();
        SEntMan.EventBus.RaiseLocalEvent(bow, ref count);
        return count.Count;
    }
}
