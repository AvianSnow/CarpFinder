using Robust.Shared.Prototypes;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
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

    private void Attacked(EntityUid hero)
    {
        SEntMan.GetComponent<CFActionPoolComponent>(hero).MapAttacks = 1;
    }

    [Test]
    public async Task BrutishShoveIsAPressWithATwoHandedWeapon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool first = true, oneHanded = true, used = false, offGuard = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFStrikeActionHero");
            var foe = Spawn("CFStrikeActionFoe", 1);
            var action = Grant(hero, "CFActBrutishShove");
            Hold(hero, "CFGreatsword");
            first = Use(hero, action, foe);

            var other = Spawn("CFStrikeActionHero");
            var same = Grant(other, "CFActBrutishShove");
            Hold(other, "CFLongsword");
            Attacked(other);
            oneHanded = Use(other, same, foe);

            Attacked(hero);
            used = Use(hero, action, foe);
            offGuard = Conditions.IsOffGuardTo(foe, hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False, "A press needs an attack before it this turn.");
            Assert.That(oneHanded, Is.False, "It needs a two-handed weapon.");
            Assert.That(used, Is.True);
            Assert.That(offGuard, Is.True);
        });
    }

    [Test]
    public async Task CombatGrabGrabsForAWhileAndSlamDownTrips()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool grabbed = false, timed = false, prone = false;
        var eased = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFStrikeActionHero");
            Hold(hero, "CFLongsword");

            var foe = Spawn("CFStrikeActionFoe", 1);
            Attacked(hero);
            Use(hero, Grant(hero, "CFActCombatGrab"), foe);
            var cond = SEntMan.GetComponent<CFConditionComponent>(foe);
            grabbed = cond.Grabbed && cond.Grabber == hero;
            timed = cond.GrabbedUntil > SGameTiming.CurTime;

            var second = Spawn("CFStrikeActionFoe", 1);
            SEntMan.GetComponent<CFAbilityScoreComponent>(second).AbilityModifiers[CFAbility.Dexterity] = -20;
            Use(hero, Grant(hero, "CFActSlamDown"), second);
            prone = Conditions.IsProne(second);

            // Another creature: Slam Down and Unsteadying Strike are both flourishes, one a turn.
            var commander = Spawn("CFStrikeActionHero");
            Hold(commander, "CFLongsword");
            var third = Spawn("CFStrikeActionFoe", 1);
            Use(commander, Grant(commander, "CFActUnsteadyingStrike"), third);
            eased = SEntMan.System<CFBonusSystem>().Total(third, CFBonusStat.ManeuverDc);
        });

        Assert.Multiple(() =>
        {
            Assert.That(grabbed, Is.True);
            Assert.That(timed, Is.True, "Combat Grab lets go after a while.");
            Assert.That(prone, Is.True, "Slam Down trips what it hurts.");
            Assert.That(eased, Is.EqualTo(-2), "Unsteadying Strike makes the next maneuver easier.");
        });
    }

    [Test]
    public async Task BespellStrikesChargesTheNextHitAfterARealSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool afterCantrip = true, afterSpell = false, afterHit = true, psiCantrip = false;
        int plain = 0, charged = 0;
        await Server.WaitPost(() =>
        {
            var spells = SEntMan.System<CFSpellSystem>();
            var strikes = SEntMan.System<CFStrikeSystem>();
            var features = SEntMan.System<CFCharacterFeatureSystem>();
            var cantrip = SEntMan.GetComponent<CFSpellComponent>(SEntMan.SpawnEntity("CFElectricArc", Robust.Shared.Map.MapCoordinates.Nullspace));
            var fear = SEntMan.GetComponent<CFSpellComponent>(SEntMan.SpawnEntity("CFFear", Robust.Shared.Map.MapCoordinates.Nullspace));

            var wizard = Spawn("CFStrikeActionHero");
            features.Apply(wizard, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFBespellStrikesWizard")).Grants, "feat:CFBespellStrikesWizard");
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(wizard);
            var foe = Spawn("CFStrikeActionFoe", 1);

            // A d1 weapon, so every hit deals the same and the extra die shows.
            var fist = strikes.UnarmedStats(wizard) with { DamageDie = 1 };
            strikes.PerformStrike(wizard, foe, fist, wizard, melee: true, countMap: false, fixedMap: 0);
            plain = Hurt(foe);

            spells.NoteBespell(wizard, cantrip);
            afterCantrip = options.BespellUntil > SGameTiming.CurTime;
            spells.NoteBespell(wizard, fear);
            afterSpell = options.BespellUntil > SGameTiming.CurTime;

            strikes.PerformStrike(wizard, foe, fist, wizard, melee: true, countMap: false, fixedMap: 0);
            charged = Hurt(foe) - plain;
            afterHit = options.BespellUntil > SGameTiming.CurTime;

            var psychic = Spawn("CFStrikeActionHero");
            features.Apply(psychic, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFPsiStrikes")).Grants, "feat:CFPsiStrikes");
            spells.NoteBespell(psychic, cantrip);
            psiCantrip = SEntMan.GetComponent<CFCombatOptionsComponent>(psychic).BespellUntil > SGameTiming.CurTime;
        });

        Assert.Multiple(() =>
        {
            Assert.That(afterCantrip, Is.False, "A cantrip does not charge Bespell Strikes.");
            Assert.That(afterSpell, Is.True);
            Assert.That(charged, Is.GreaterThan(plain), "The charged hit deals the extra die.");
            Assert.That(afterHit, Is.False, "One hit uses it up.");
            Assert.That(psiCantrip, Is.True, "Psi Strikes works after a cantrip too.");
        });
    }

    [Test]
    public async Task SwipeAndQuickReversalStrikeASecondEnemy()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool lone = true, reversed = false;
        int swipedFirst = 0, swipedSecond = 0, far = -1, attacks = 0, reversedSecond = 0, attacksAfterReversal = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFStrikeActionHero");
            Hold(hero, "CFLongsword");
            var first = Spawn("CFStrikeActionFoe", 1);
            var beside = SEntMan.SpawnEntity("CFStrikeActionFoe", _map.Local(1, 1));
            var distant = Spawn("CFStrikeActionFoe", 5);

            Use(hero, Grant(hero, "CFActSwipe"), first);
            swipedFirst = Hurt(first);
            swipedSecond = Hurt(beside);
            far = Hurt(distant);
            attacks = SEntMan.GetComponent<CFActionPoolComponent>(hero).MapAttacks;

            var alone = Spawn("CFStrikeActionHero", 5);
            Hold(alone, "CFLongsword");
            Attacked(alone);
            lone = Use(alone, Grant(alone, "CFActQuickReversal"), distant);

            var fighter = Spawn("CFStrikeActionHero");
            Hold(fighter, "CFLongsword");
            Attacked(fighter);
            var before = Hurt(beside);
            reversed = Use(fighter, Grant(fighter, "CFActQuickReversal"), first);
            reversedSecond = Hurt(beside) - before;
            attacksAfterReversal = SEntMan.GetComponent<CFActionPoolComponent>(fighter).MapAttacks;
        });

        Assert.Multiple(() =>
        {
            Assert.That(swipedFirst, Is.GreaterThan(0));
            Assert.That(swipedSecond, Is.GreaterThan(0), "The enemy beside the target is hit too.");
            Assert.That(far, Is.Zero, "Nobody out of reach.");
            Assert.That(attacks, Is.EqualTo(2), "A Swipe counts as two attacks.");
            Assert.That(lone, Is.False, "Quick Reversal needs a second enemy in reach.");
            Assert.That(reversed, Is.True);
            Assert.That(reversedSecond, Is.GreaterThan(0));
            Assert.That(attacksAfterReversal, Is.EqualTo(2), "The one before it and its first Strike: the second is free.");
        });
    }

    [Test]
    public async Task DefensiveAdvanceRaisesTheShieldRushesInAndStrikes()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        var raised = false;
        var hurt = 0;
        await Server.WaitPost(() =>
        {
            hero = Spawn("CFStrikeActionHero");
            foe = Spawn("CFStrikeActionFoe", 4);
            var shield = Spawn("CFSteelShield");
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, shield);
            var action = Grant(hero, "CFActDefensiveAdvance");
            Use(hero, action, foe);
            raised = SEntMan.GetComponent<CFShieldComponent>(shield).Raised;
            hurt = Hurt(foe);
        });
        await Pair.RunSeconds(1.5f);

        var distance = 0f;
        await Server.WaitPost(() =>
        {
            var xform = SEntMan.System<SharedTransformSystem>();
            distance = (xform.GetWorldPosition(foe) - xform.GetWorldPosition(hero)).Length();
        });

        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.True);
            Assert.That(hurt, Is.GreaterThan(0), "The Strike at the end of the rush.");
            Assert.That(distance, Is.LessThan(2.5f), "It started 4 tiles away and closed in.");
        });
    }

    [Test]
    public async Task AParryNeedsTheRightThingInHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool duelWithGreatsword = true, duelWithSword = false, haftWithSword = true, haftWithGreatsword = false;
        var ac = 0;
        await Server.WaitPost(() =>
        {
            var duelist = Spawn("CFStrikeActionHero");
            var duel = Grant(duelist, "CFActDuelingParry");
            var haft = Grant(duelist, "CFActRaiseHaft");
            Hold(duelist, "CFLongsword");
            duelWithSword = CanUse(duelist, duel, duelist);
            haftWithSword = CanUse(duelist, haft, duelist);
            SEntMan.System<SharedActionsSystem>().PerformAction(duelist, duel, new CFActivityInstantEvent());
            ac = SEntMan.System<CFBonusSystem>().Total(duelist, CFBonusStat.ArmorClass);

            var guardian = Spawn("CFStrikeActionHero");
            Hold(guardian, "CFGreatsword");
            duelWithGreatsword = CanUse(guardian, Grant(guardian, "CFActDuelingParry"), guardian);
            haftWithGreatsword = CanUse(guardian, Grant(guardian, "CFActRaiseHaft"), guardian);
        });

        Assert.Multiple(() =>
        {
            Assert.That(duelWithSword, Is.True);
            Assert.That(ac, Is.EqualTo(2), "Dueling Parry is +2 to AC.");
            Assert.That(haftWithSword, Is.False, "Raise Haft needs a two-handed weapon.");
            Assert.That(duelWithGreatsword, Is.False, "Dueling Parry needs a one-handed weapon.");
            Assert.That(haftWithGreatsword, Is.True);
        });
    }

    [Test]
    public async Task RapidResponseRunsToAFallenAlly()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid cleric = default, ally = default;
        await Server.WaitPost(() =>
        {
            cleric = Spawn("CFStrikeActionHero");
            SEntMan.System<CFCharacterFeatureSystem>().Apply(
                cleric,
                Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFRapidResponse")).Grants,
                "feat:CFRapidResponse");
            ally = Spawn("CFStrikeActionHero", 5);
            SEntMan.System<CFReactionSystem>().AnswerAllyDropped(ally);
        });
        await Pair.RunSeconds(1.5f);

        var distance = 0f;
        await Server.WaitPost(() =>
        {
            var xform = SEntMan.System<SharedTransformSystem>();
            distance = (xform.GetWorldPosition(ally) - xform.GetWorldPosition(cleric)).Length();
        });

        Assert.That(distance, Is.LessThan(2.5f), "It started 5 tiles away and ran to the ally's side.");
    }

    [Test]
    public async Task QuickDrawTakesTheWeaponOffTheBeltAndStrikes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool empty = true, worn = false, used = false, inHand = false;
        var hurt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFStrikeActionHero");
            var foe = Spawn("CFStrikeActionFoe", 1);
            var action = Grant(hero, "CFActQuickDraw");
            empty = CanUse(hero, action, foe) && Use(hero, action, foe) && Hurt(foe) > 0;

            var sword = Spawn("CFLongsword");
            worn = SEntMan.System<Content.Shared.Inventory.InventorySystem>().TryEquip(hero, sword, "belt", silent: true, force: true);
            used = Use(hero, action, foe);
            hurt = Hurt(foe);
            inHand = SEntMan.System<SharedHandsSystem>().IsHolding(hero, sword);
        });

        Assert.Multiple(() =>
        {
            Assert.That(empty, Is.False, "Nothing to draw.");
            Assert.That(worn, Is.True);
            Assert.That(used, Is.True);
            Assert.That(inHand, Is.True, "The sword left the belt for a hand.");
            Assert.That(hurt, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task AggressiveBlockShovesTheAttackerAway()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        await Server.WaitPost(() =>
        {
            hero = Spawn("CFStrikeActionHero");
            foe = Spawn("CFStrikeActionFoe", 1);
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            options.ShieldBlock = true;
            options.AggressiveBlock = true;
            var shield = Spawn("CFSteelShield");
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, shield);
            SEntMan.System<CFShieldSystem>().SetRaised((shield, SEntMan.GetComponent<CFShieldComponent>(shield)), true);

            var strikes = SEntMan.System<CFStrikeSystem>();
            strikes.PerformStrike(foe, hero, strikes.UnarmedStats(foe), foe, melee: true, countMap: false, fixedMap: 0);
        });
        await Pair.RunSeconds(1f);

        var distance = 0f;
        await Server.WaitPost(() =>
        {
            var xform = SEntMan.System<SharedTransformSystem>();
            distance = (xform.GetWorldPosition(foe) - xform.GetWorldPosition(hero)).Length();
        });

        Assert.That(distance, Is.GreaterThan(1.2f), "Shoved back from 1 tile away.");
    }

    private int Arrows(EntityUid bow)
    {
        var count = new GetAmmoCountEvent();
        SEntMan.EventBus.RaiseLocalEvent(bow, ref count);
        return count.Count;
    }
}
