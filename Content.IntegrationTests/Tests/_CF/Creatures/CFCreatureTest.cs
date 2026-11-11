using System;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CombatMode;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>The level table a Recall Knowledge DC comes from. Plain math, so no game runs.</summary>
[TestFixture]
public sealed class CFLevelDcTest
{
    [Test]
    public void TheTableStartsWhereTheBookDoes()
    {
        Assert.That(CFCraftingRules.LevelDc(-1), Is.EqualTo(13));
    }
}

/// <summary>
/// Stat blocks, and what makes a monster different from a character. Real monsters get rebalanced, so a test here that needs
/// a particular number uses one of the test copies below, which keep their numbers whatever happens to the original.
/// </summary>
[TestFixture]
[TestOf(typeof(CFStrikeSystem))]
public sealed class CFCreatureTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCreatureTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFExperience

- type: entity
  parent: CFMobGiantRat
  id: CFCreatureTestRat
  components:
  - type: CFCombatant
    level: -1
    maxHp: 8
    currentHp: 8
    diesAtZeroHp: true

- type: entity
  parent: CFMobBoar
  id: CFCreatureTestBoar
  components:
  - type: CFCombatant
    printedAttack: 10
    printedDamage: 4
  - type: CFCombatOptions
    naturalDice: 2
    naturalDie: 6

- type: entity
  parent: CFMobSewerOoze
  id: CFCreatureTestOoze
  components:
  - type: CFCombatant
    maxHp: 40
    currentHp: 40
  - type: CFResistances
    entries:
    - damageType: Acid
      amount: 9999
";

    private CFDevMap _map = default!;

    private (EntityUid Uid, CFCombatantComponent Combat) Creature(string id, int x = 0)
    {
        var uid = SEntMan.SpawnEntity(id, _map.Local(x, 0));
        return (uid, SEntMan.GetComponent<CFCombatantComponent>(uid));
    }

    /// <summary>
    /// Monsters get rebalanced, so this never names a number. Each one is compared with its own YAML: whatever the stat
    /// block says has to be what the game uses. A new monster is covered without adding anything here.
    /// </summary>
    [Test]
    public async Task EveryMonsterPlaysByItsOwnStatBlock()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var problems = new List<string>();
        var count = 0;
        await Server.WaitPost(() =>
        {
            var prototypes = Server.ProtoMan;
            var factory = Server.ResolveDependency<IComponentFactory>();
            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract
                    || proto.ID.Contains("Test")
                    || !proto.TryComp<CFCombatantComponent>(out var book, factory)
                    || !book.DiesAtZeroHp)
                    continue;

                count++;
                CheckStatBlock(proto.ID, book, problems);
            }
        });

        Assert.That(count, Is.GreaterThan(0), "No monsters were found to check.");
        Assert.That(problems, Is.Empty);
    }

    private void CheckStatBlock(string id, CFCombatantComponent book, List<string> problems)
    {
        void Same(string what, int? expected, int actual)
        {
            if (expected != null && expected != actual)
                problems.Add($"{id}: {what} is {actual} in the game and {expected} in its YAML");
        }

        var prototypes = Server.ProtoMan;
        var (uid, combat) = Creature(id);
        var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(uid);
        var stats = SEntMan.System<CFStrikeSystem>().UnarmedStats(uid);

        Same("level", book.Level, combat.Level);
        Same("AC", book.ArmorClass, combat.ArmorClass);
        Same("max HP", book.MaxHp, combat.MaxHp);
        Same("starting HP", combat.MaxHp, combat.CurrentHp);
        Same("Fortitude", book.PrintedFortitude, CFDefense.SaveModifier(prototypes, combat, abilities, CFDefenseType.Fortitude));
        Same("Reflex", book.PrintedReflex, CFDefense.SaveModifier(prototypes, combat, abilities, CFDefenseType.Reflex));
        Same("Will", book.PrintedWill, CFDefense.SaveModifier(prototypes, combat, abilities, CFDefenseType.Will));
        Same("Perception", book.PrintedPerception, CFDefense.PerceptionModifier(prototypes, combat, abilities));
        Same("attack", book.PrintedAttack, CFMelee.AttackBonus(abilities, combat, stats, CFProficiencyRank.Trained));
        Same("damage", book.PrintedDamage, CFMelee.DamageBonus(abilities, stats, combat));

        foreach (var (skill, printed) in book.PrintedSkills)
        {
            Same(skill, printed, CFDefense.SkillModifier(prototypes, combat, abilities, skill, CFAbility.Strength));
        }

        if (!SEntMan.TryGetComponent(uid, out CFCombatOptionsComponent natural) || natural.NaturalDie <= 0)
            return;

        Same("damage dice", Math.Max(1, natural.NaturalDice), stats.DamageDice);
        Same("damage die", natural.NaturalDie, stats.DamageDie);
        if (stats.Traits.Nonlethal)
            problems.Add($"{id}: natural attacks are lethal, but this one is nonlethal");
    }

    [Test]
    public async Task MonstersDieAtZeroAndAwardXp()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (rat, combat) = Creature("CFCreatureTestRat");
            var hero = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            var before = xp.Xp;

            SEntMan.System<CFEffectSystem>().DealDamage(hero, rat, 100, "Slashing", false);

            Assert.That(SEntMan.System<MobStateSystem>().IsDead(rat), "PF2e monsters die at 0 HP.");
            Assert.That(combat.Dying, Is.Zero);
            Assert.That(xp.Xp - before, Is.EqualTo(20), "A creature two levels below the party is worth 20 XP.");
        });
    }

    [Test]
    public async Task CharactersStillGoDyingAtZero()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (rat, _) = Creature("CFCreatureTestRat");
            var hero = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));

            SEntMan.System<CFEffectSystem>().DealDamage(rat, hero, 1000, "Slashing", false);

            Assert.That(SEntMan.System<MobStateSystem>().IsDead(hero), Is.False);
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).Dying, Is.GreaterThan(0));
        });
    }

    /// <summary>A CF rule change: PF2e leaves the target unconscious at 0 HP.</summary>
    [Test]
    public async Task StabilizeWakesTheDyingAtOneHp()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var caster = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(0, 0));
            var patient = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));
            var combat = SEntMan.GetComponent<CFCombatantComponent>(patient);
            var effects = SEntMan.System<CFEffectSystem>();
            var spell = SEntMan.SpawnEntity("CFStabilize", MapCoordinates.Nullspace);

            effects.DealDamage(caster, patient, 1000, "Slashing", false);
            Assert.That(combat.Dying, Is.GreaterThan(0));

            effects.DeliverToTarget(spell, caster, patient);

            Assert.Multiple(() =>
            {
                Assert.That(combat.CurrentHp, Is.EqualTo(1));
                Assert.That(combat.Dying, Is.Zero);
                Assert.That(combat.Wounded, Is.Zero, "The spell does not add wounded.");
                Assert.That(SEntMan.System<MobStateSystem>().IsAlive(patient));
            });
        });
    }

    [Test]
    public async Task StabilizeLeavesTheHealthyAlone()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var caster = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(0, 0));
            var patient = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));
            var spell = SEntMan.SpawnEntity("CFStabilize", MapCoordinates.Nullspace);

            SEntMan.System<CFEffectSystem>().DeliverToTarget(spell, caster, patient);

            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(patient).CurrentHp, Is.EqualTo(500));
        });
    }

    [Test]
    public async Task ImmunitiesLastAndWork()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var (ooze, combat) = (default(EntityUid), default(CFCombatantComponent)!);
        await Server.WaitPost(() => (ooze, combat) = Creature("CFCreatureTestOoze"));

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var effects = SEntMan.System<CFEffectSystem>();
            effects.DealDamage(ooze, ooze, 10, "Acid", false);
            Assert.That(combat.CurrentHp, Is.EqualTo(40), "Sewer ooze is immune to acid.");

            effects.DealDamage(ooze, ooze, 10, "Bludgeoning", false);
            Assert.That(combat.CurrentHp, Is.EqualTo(30));
        });
    }

    [Test]
    public async Task CreatureStrikeUsesItsNaturalAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (boar, _) = Creature("CFCreatureTestBoar");
            var hero = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));
            var strikes = SEntMan.System<CFStrikeSystem>();
            var defender = SEntMan.GetComponent<CFCombatantComponent>(hero);

            // AC 10 against +10: only a natural 1 misses. 2d6+4 is 6 to 16, doubled on a critical.
            for (var i = 0; i < 10 && defender.CurrentHp == 500; i++)
            {
                strikes.PerformStrike(boar, hero, strikes.UnarmedStats(boar), boar, melee: true, countMap: false);
            }

            Assert.That(500 - defender.CurrentHp, Is.InRange(6, 32));
        });
    }

    [Test]
    public async Task ARealSwingLandsAsAPathfinderStrike()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid boar = default;
        EntityUid hero = default;

        await Server.WaitPost(() =>
        {
            (boar, _) = Creature("CFCreatureTestBoar");
            hero = SEntMan.SpawnEntity("CFCreatureTestHero", _map.Local(1, 0));
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(boar, true);
        });

        var melee = SEntMan.System<SharedMeleeWeaponSystem>();
        for (var i = 0; i < 10; i++)
        {
            await Server.WaitPost(() =>
            {
                Assert.That(melee.TryGetWeapon(boar, out var weaponUid, out var weapon));
                melee.AttemptLightAttack(boar, weaponUid, weapon, hero);
            });

            await Pair.RunSeconds(1.2f);

            if (SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp < 500)
                break;
        }

        await Server.WaitAssertion(() =>
            Assert.That(500 - SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.InRange(6, 32)));
    }
}
