using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Reactions from <c>CFGrantReaction</c>, using the real feats. The dice are passed in, so each result is exact.</summary>
[TestFixture]
[TestOf(typeof(CFReactionSystem))]
public sealed class CFReactionTest : GameTest
{
    private const int ArmorClass = 14;

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFReactionTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 12
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFReactionTestHero
  id: CFReactionTestFoe
  components:
  - type: CFCombatant
    printedAttack: 0
    printedDamage: 0
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFReactionTestHero
  id: CFReactionTestMonster
  components:
  - type: CFCombatant
    diesAtZeroHp: true
";

    private CFDevMap _map = default!;

    private CFReactionSystem Reactions => SEntMan.System<CFReactionSystem>();

    private EntityUid Hero(params string[] feats)
    {
        var hero = Spawn("CFReactionTestHero");
        Learn(hero, feats);
        return hero;
    }

    private EntityUid Spawn(string id)
    {
        return SEntMan.SpawnEntity(id, _map.Local(0, 0));
    }

    private void Learn(EntityUid mob, params string[] feats)
    {
        var features = SEntMan.System<CFCharacterFeatureSystem>();
        foreach (var id in feats)
        {
            features.Apply(mob, Server.ProtoMan.Index<CFFeatPrototype>(id).Grants, $"feat:{id}");
        }
    }

    private CFCombatOptionsComponent Options(EntityUid uid)
    {
        return SEntMan.GetComponent<CFCombatOptionsComponent>(uid);
    }

    /// <summary>Returns the result and the AC it was decided against.</summary>
    private (CFDegree Degree, int Ac) Attack(EntityUid target, EntityUid attacker, int total, bool ranged = false)
    {
        var ac = ArmorClass;
        var degree = CFDegrees.DegreeOfSuccess(10, total - 10, ac);
        return (Reactions.AnswerAttack(target, attacker, ranged, 10, total - 10, ref ac, degree), ac);
    }

    [Test]
    public async Task ASpellAttackCanBeDodgedButNotDeflected()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFDegree dodged = default, deflected = default;
        await Server.WaitPost(() =>
        {
            var effects = SEntMan.System<CFEffectSystem>();
            dodged = effects.AttackDegree(Spawn("CFReactionTestFoe"), Hero("CFNimbleDodge"), 10, 5, 15);
            deflected = effects.AttackDegree(Spawn("CFReactionTestFoe"), Hero("CFDeflectProjectile"), 10, 5, 15);
        });

        Assert.Multiple(() =>
        {
            Assert.That(dodged, Is.EqualTo(CFDegree.Failure), "A hit by 0 turns into a miss with +2 AC.");
            Assert.That(deflected, Is.EqualTo(CFDegree.Success), "A spell is not a projectile.");
        });
    }

    [Test]
    public async Task ADodgeFiresOnlyWhenItChangesTheResult()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        (CFDegree Degree, int Ac) tooStrong = default, turned = default, again = default;
        int spentAfterTooStrong = -1, spentAfterTurned = 0;
        await Server.WaitPost(() =>
        {
            var hero = Hero("CFNimbleDodge");
            var foe = Spawn("CFReactionTestFoe");

            tooStrong = Attack(hero, foe, 20);
            spentAfterTooStrong = Options(hero).ReactionSpent;

            turned = Attack(hero, foe, 15);
            spentAfterTurned = Options(hero).ReactionSpent;

            again = Attack(hero, foe, 15);
        });

        Assert.Multiple(() =>
        {
            Assert.That(tooStrong.Degree, Is.EqualTo(CFDegree.Success));
            Assert.That(tooStrong.Ac, Is.EqualTo(ArmorClass));
            Assert.That(spentAfterTooStrong, Is.Zero, "+2 would not have helped, so it is kept.");
            Assert.That(turned.Degree, Is.EqualTo(CFDegree.Failure));
            Assert.That(turned.Ac, Is.EqualTo(ArmorClass + 2));
            Assert.That(spentAfterTurned, Is.Not.Zero);
            Assert.That(again.Degree, Is.EqualTo(CFDegree.Success), "One reaction until the pool is next full.");
        });
    }

    private int Used(EntityUid uid, string feat)
    {
        return SEntMan.GetComponent<CFReactionsComponent>(uid).Known.First(known => known.Key == $"feat:{feat}").UsedToday;
    }

    [Test]
    public async Task AHigherLevelReactionComesFirstAndTheOrderCanBeChanged()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        List<string> before = default!, after = default!;
        var refused = true;
        await Server.WaitPost(() =>
        {
            var hero = Hero("CFNimbleDodge", "CFScarsofSteel");
            var order = SEntMan.GetComponent<CFReactionOrderComponent>(hero);
            before = order.Entries.Select(entry => entry.Key).ToList();

            refused = !Reactions.Reorder(hero, new List<string> { "feat:CFNimbleDodge", "feat:NoSuchFeat" });
            Reactions.Reorder(hero, new List<string> { "feat:CFNimbleDodge", "feat:CFScarsofSteel" });
            after = order.Entries.Select(entry => entry.Key).ToList();
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(new[] { "feat:CFScarsofSteel", "feat:CFNimbleDodge" }), "Level 4 goes above level 1.");
            Assert.That(refused, Is.True, "Only the same reactions in a new order are taken.");
            Assert.That(after, Is.EqualTo(new[] { "feat:CFNimbleDodge", "feat:CFScarsofSteel" }));
        });
    }

    [Test]
    public async Task TheFirstReactionThatHelpsIsTheOneThatFires()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int deflectByDefault = 0, dodgeWhenMoved = 0, deflectWhenMoved = 0;
        await Server.WaitPost(() =>
        {
            // Both would turn this shot into a miss, so only the order decides. Deflect Projectile is level 4, so it is first.
            var first = Hero("CFNimbleDodge", "CFDeflectProjectile");
            Attack(first, Spawn("CFReactionTestFoe"), ArmorClass, ranged: true);
            deflectByDefault = Used(first, "CFDeflectProjectile");

            var second = Hero("CFNimbleDodge", "CFDeflectProjectile");
            Reactions.Reorder(second, new List<string> { "feat:CFNimbleDodge", "feat:CFDeflectProjectile" });
            Attack(second, Spawn("CFReactionTestFoe"), ArmorClass, ranged: true);
            dodgeWhenMoved = Used(second, "CFNimbleDodge");
            deflectWhenMoved = Used(second, "CFDeflectProjectile");
        });

        Assert.Multiple(() =>
        {
            Assert.That(deflectByDefault, Is.EqualTo(1));
            Assert.That(dodgeWhenMoved, Is.EqualTo(1));
            Assert.That(deflectWhenMoved, Is.Zero, "One reaction, and the first in line had it.");
        });
    }

    [Test]
    public async Task ScarsOfSteelTakesThePhysicalPartOfACriticalHit()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int fire = 0, slash = 0;
        await Server.WaitPost(() =>
        {
            var hero = Hero("CFScarsofSteel");
            SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Constitution] = 3;
            var foe = Spawn("CFReactionTestFoe");

            fire = Reactions.AnswerCriticalHit(hero, foe, 20, "Fire");
            slash = Reactions.AnswerCriticalHit(hero, foe, 20, "Slashing");
        });

        Assert.Multiple(() =>
        {
            Assert.That(fire, Is.EqualTo(20), "Only physical damage.");
            Assert.That(slash, Is.EqualTo(17), "Constitution 3 plus half of level 1.");
        });
    }

    [Test]
    public async Task GuardiansDeflectionTurnsAHitOnAnAllyIntoAMiss()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        (CFDegree Degree, int Ac) result = default;
        var used = 0;
        await Server.WaitPost(() =>
        {
            var guardian = Hero("CFGuardiansDeflectionSwashbuckler");
            var ally = Hero();
            result = Attack(ally, Spawn("CFReactionTestFoe"), ArmorClass);
            used = Used(guardian, "CFGuardiansDeflectionSwashbuckler");
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Degree, Is.EqualTo(CFDegree.Failure));
            Assert.That(result.Ac, Is.EqualTo(ArmorClass + 2));
            Assert.That(used, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ACriticalThatBecomesAHitStillCounts()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        (CFDegree Degree, int Ac) result = default;
        await Server.WaitPost(() => result = Attack(Hero("CFNimbleDodge"), Spawn("CFReactionTestFoe"), 24));

        Assert.That(result.Degree, Is.EqualTo(CFDegree.Success));
    }

    [Test]
    public async Task ItPicksTheReactionThatHelps()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        (CFDegree Degree, int Ac) shot = default, swing = default;
        await Server.WaitPost(() =>
        {
            shot = Attack(Hero("CFNimbleDodge", "CFDeflectProjectile"), Spawn("CFReactionTestFoe"), 17, ranged: true);
            swing = Attack(Hero("CFNimbleDodge", "CFDeflectProjectile"), Spawn("CFReactionTestFoe"), 17);
        });

        Assert.Multiple(() =>
        {
            Assert.That(shot.Degree, Is.EqualTo(CFDegree.Failure), "Only Deflect Projectile's +4 is enough.");
            Assert.That(shot.Ac, Is.EqualTo(ArmorClass + 4));
            Assert.That(swing.Degree, Is.EqualTo(CFDegree.Success), "Deflect Projectile is for ranged attacks.");
            Assert.That(swing.Ac, Is.EqualTo(ArmorClass));
        });
    }

    [Test]
    public async Task CircumstanceBonusesDoNotStack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        (CFDegree Degree, int Ac) equal = default, smaller = default;
        await Server.WaitPost(() =>
        {
            var covered = Hero("CFNimbleDodge");
            SEntMan.System<CFBonusSystem>().AddBonus(covered, CFBonusStat.ArmorClass, CFBonusType.Circumstance, 2, 60f);
            equal = Attack(covered, Spawn("CFReactionTestFoe"), 15);

            var slight = Hero("CFNimbleDodge");
            SEntMan.System<CFBonusSystem>().AddBonus(slight, CFBonusStat.ArmorClass, CFBonusType.Circumstance, 1, 60f);
            smaller = Attack(slight, Spawn("CFReactionTestFoe"), 14);
        });

        Assert.Multiple(() =>
        {
            Assert.That(equal.Ac, Is.EqualTo(ArmorClass), "A +2 already there leaves nothing to add.");
            Assert.That(smaller.Ac, Is.EqualTo(ArmorClass + 1), "Only the part above a +1 counts.");
            Assert.That(smaller.Degree, Is.EqualTo(CFDegree.Failure));
        });
    }

    [Test]
    public async Task DeflectProjectileNeedsAFreeHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        (CFDegree Degree, int Ac) result = default;
        await Server.WaitPost(() =>
        {
            var hero = Hero("CFDeflectProjectile");
            var hands = SEntMan.System<SharedHandsSystem>();
            hands.TryPickupAnyHand(hero, Spawn("CFDagger"));
            hands.TryPickupAnyHand(hero, Spawn("CFDagger"));
            result = Attack(hero, Spawn("CFReactionTestFoe"), 15, ranged: true);
        });

        Assert.That(result.Degree, Is.EqualTo(CFDegree.Success));
    }

    [Test]
    public async Task FlashyDodgeGivesPanacheOnlyOnAMiss()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool afterMiss = false, afterGraze = true;
        await Server.WaitPost(() =>
        {
            var hero = Hero("CFFlashyDodge");
            Attack(hero, Spawn("CFReactionTestFoe"), 15);
            afterMiss = Options(hero).Panache;

            var grazed = Hero("CFFlashyDodge");
            Attack(grazed, Spawn("CFReactionTestFoe"), 24);
            afterGraze = Options(grazed).Panache;
        });

        Assert.Multiple(() =>
        {
            Assert.That(afterMiss, Is.True);
            Assert.That(afterGraze, Is.False, "The dodge turned a critical into a hit, and the attack still landed.");
        });
    }

    [Test]
    public async Task CharmedLifeImprovesASaveAndKeepsItsReactionOtherwise()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFDegree helped = default, hopeless = default, perception = default;
        int total = 14, keptSpent = -1;
        var panache = false;
        await Server.WaitPost(() =>
        {
            var hero = Hero("CFCharmedLife");
            helped = Reactions.AnswerSave(hero, CFDefenseType.Will, 10, 4, 15, ref total, CFDegree.Failure);
            panache = Options(hero).Panache;

            var other = Hero("CFCharmedLife");
            var ignored = 11;
            hopeless = Reactions.AnswerSave(other, CFDefenseType.Will, 10, 1, 15, ref ignored, CFDegree.Failure);
            perception = Reactions.AnswerSave(other, CFDefenseType.Perception, 10, 4, 15, ref ignored, CFDegree.Failure);
            keptSpent = Options(other).ReactionSpent;
        });

        Assert.Multiple(() =>
        {
            Assert.That(helped, Is.EqualTo(CFDegree.Success));
            Assert.That(total, Is.EqualTo(16));
            Assert.That(panache, Is.True);
            Assert.That(hopeless, Is.EqualTo(CFDegree.Failure));
            Assert.That(perception, Is.EqualTo(CFDegree.Failure), "Perception is not a saving throw.");
            Assert.That(keptSpent, Is.Zero);
        });
    }

    [Test]
    public async Task OrcFerocityWorksOncePerLongRest()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int hpFirst = 0, woundedFirst = 0, dyingFirst = -1, dyingSecond = 0, hpRested = 0;
        var alive = false;
        await Server.WaitPost(() =>
        {
            var hero = Hero("CFOrcFerocity");
            var foe = Spawn("CFReactionTestFoe");
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var effects = SEntMan.System<CFEffectSystem>();

            effects.DealDamage(foe, hero, 10000, "Slashing", false);
            hpFirst = combat.CurrentHp;
            woundedFirst = combat.Wounded;
            dyingFirst = combat.Dying;
            alive = SEntMan.System<MobStateSystem>().IsAlive(hero);

            Options(hero).ReactionSpent = 0;
            effects.DealDamage(foe, hero, 10000, "Slashing", false);
            dyingSecond = combat.Dying;

            var rester = Hero("CFOrcFerocity");
            var rested = SEntMan.GetComponent<CFCombatantComponent>(rester);
            effects.DealDamage(foe, rester, 10000, "Slashing", false);
            SEntMan.System<CFLongRestSystem>().LongRest(rester);
            Options(rester).ReactionSpent = 0;
            effects.DealDamage(foe, rester, 10000, "Slashing", false);
            hpRested = rested.CurrentHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(hpFirst, Is.EqualTo(1));
            Assert.That(woundedFirst, Is.EqualTo(1));
            Assert.That(dyingFirst, Is.Zero);
            Assert.That(alive, Is.True);
            Assert.That(dyingSecond, Is.GreaterThan(0), "It has been used today.");
            Assert.That(hpRested, Is.EqualTo(1), "A long rest gives it back.");
        });
    }

    [Test]
    public async Task OrcFerocityCannotSaveWhatDiesOutright()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var dead = false;
        await Server.WaitPost(() =>
        {
            var monster = Spawn("CFReactionTestMonster");
            Learn(monster, "CFOrcFerocity");
            SEntMan.System<CFEffectSystem>().DealDamage(Spawn("CFReactionTestFoe"), monster, 10000, "Slashing", false);
            dead = SEntMan.System<MobStateSystem>().IsDead(monster);
        });

        Assert.That(dead, Is.True);
    }

    [Test]
    public async Task WoundedRageNeedsToBeAbleToRage()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool raging = false, plain = true;
        var plainSpent = -1;
        await Server.WaitPost(() =>
        {
            var effects = SEntMan.System<CFEffectSystem>();
            var foe = Spawn("CFReactionTestFoe");

            var barbarian = Hero("CFWoundedRage");
            Options(barbarian).CanRage = true;
            effects.DealDamage(foe, barbarian, 5, "Slashing", false);
            raging = Options(barbarian).Raging;

            var other = Hero("CFWoundedRage");
            effects.DealDamage(foe, other, 5, "Slashing", false);
            plain = Options(other).Raging;
            plainSpent = Options(other).ReactionSpent;
        });

        Assert.Multiple(() =>
        {
            Assert.That(raging, Is.True);
            Assert.That(plain, Is.False);
            Assert.That(plainSpent, Is.Zero);
        });
    }

    /// <summary>
    /// A real Strike reaches the reaction. Attack +0 against AC 12: a 12 or 13 on the die is the only roll a +2 can turn, so
    /// it is bound to come up within this many swings.
    /// </summary>
    [Test]
    public async Task ARealStrikeCanBeDodged()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var spent = 0;
        await Server.WaitPost(() =>
        {
            var hero = Hero("CFNimbleDodge");
            var foe = Spawn("CFReactionTestFoe");
            var strikes = SEntMan.System<CFStrikeSystem>();
            var stats = strikes.UnarmedStats(foe);
            for (var i = 0; i < 400 && spent == 0; i++)
            {
                strikes.PerformStrike(foe, hero, stats, foe, melee: true, countMap: false);
                spent = Options(hero).ReactionSpent;
            }
        });

        Assert.That(spent, Is.Not.Zero);
    }
}
