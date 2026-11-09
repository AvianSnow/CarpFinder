using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Which attacks Sneak Attack works with, and the Flurry edge's table. Plain math, so no game runs.</summary>
[TestFixture]
public sealed class CFWave2RulesTest
{
    private static CFStrikeWeaponStats Weapon(string proficiency, int die, bool finesse = false, bool ranged = false, bool thrown = false, bool unarmed = false)
    {
        return new CFStrikeWeaponStats(1, die, "Slashing", proficiency, new CFWeaponTraits { Finesse = finesse, Unarmed = unarmed }, Ranged: ranged, ThrownStrike: thrown);
    }

    private const string Simple = "SimpleWeapons";
    private const string Martial = "MartialWeapons";

    private static IEnumerable<TestCaseData> Sneak()
    {
        yield return new TestCaseData(Weapon(Martial, 8, finesse: true), false).Returns(true).SetName("Finesse works");
        yield return new TestCaseData(Weapon(Martial, 8), false).Returns(false).SetName("A plain martial weapon does not");
        yield return new TestCaseData(Weapon(Martial, 8, ranged: true), false).Returns(true).SetName("A bow works");
        yield return new TestCaseData(Weapon(Martial, 6, ranged: true, thrown: true), false).Returns(false).SetName("A thrown weapon needs finesse");
        yield return new TestCaseData(Weapon(Martial, 6, finesse: true, ranged: true, thrown: true), false).Returns(true).SetName("A finesse thrown weapon works");
        yield return new TestCaseData(Weapon(Simple, 8), true).Returns(true).SetName("Ruffian with a d8 simple weapon");
        yield return new TestCaseData(Weapon(Simple, 10), true).Returns(false).SetName("Ruffian with a d10 simple weapon");
        yield return new TestCaseData(Weapon(Martial, 6), true).Returns(true).SetName("Ruffian with a d6 martial weapon");
        yield return new TestCaseData(Weapon(Martial, 8), true).Returns(false).SetName("Ruffian with a d8 martial weapon");
        yield return new TestCaseData(Weapon("Unarmed", 4, unarmed: true), true).Returns(false).SetName("Ruffian does not extend to fists");
    }

    [TestCaseSource(nameof(Sneak))]
    public bool SneakAttackLimits(CFStrikeWeaponStats weapon, bool ruffian)
    {
        return CFStrikeSystem.SneakAttackFits(new CFCombatOptionsComponent { SneakAttack = true, AnyWeaponSneak = ruffian }, weapon);
    }

    [TestCase(0, 0)]
    [TestCase(-5, -3)]
    [TestCase(-10, -6)]
    [TestCase(-4, -2)]
    [TestCase(-8, -4)]
    public void FlurryEdgeTable(int normal, int expected)
    {
        Assert.That(CFMelee.PreyFlurryPenalty(normal), Is.EqualTo(expected));
    }
}

/// <summary>Fighter, Rogue and Ranger features and feats, on the real data. Strength and Dexterity 40 mean a Strike always hits, and critically hits unless the die shows a 1.</summary>
[TestFixture]
[TestOf(typeof(CFStrikeSystem))]
public sealed class CFWave2Test : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFWave2Hero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 12
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
      Dexterity: 40
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFWave2Hero
  id: CFWave2Foe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFStrikeSystem Strikes => SEntMan.System<CFStrikeSystem>();

    private EntityUid Spawn(string id, int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private void Learn(EntityUid mob, string feat)
    {
        SEntMan.System<CFCharacterFeatureSystem>().Apply(mob, Server.ProtoMan.Index<CFFeatPrototype>(feat).Grants, $"feat:{feat}");
    }

    private CFCombatOptionsComponent Options(EntityUid mob)
    {
        return SEntMan.GetComponent<CFCombatOptionsComponent>(mob);
    }

    private int Hurt(EntityUid mob)
    {
        return StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;
    }

    private EntityUid Built(CFCharacterBuildData build)
    {
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));
        return hero;
    }

    private void LevelTo(EntityUid hero, int level)
    {
        var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
        SEntMan.System<CFExperienceSystem>().AddXp(hero, (level - combat.Level) * CFExperienceComponent.XpPerLevel);
    }

    /// <summary>A shot with one fixed damage die, so anything above the base is extra damage.</summary>
    private static CFStrikeWeaponStats Bow()
    {
        return new CFStrikeWeaponStats(1, 1, "Piercing", "MartialWeapons", new CFWeaponTraits(), Ranged: true, RangeIncrementFeet: 100);
    }

    [Test]
    public async Task PreyPrecisionWorksAtRangeOncePerTurn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        // The bow's one fixed die is 1, or 2 doubled on a critical hit, so more than that is the extra damage.
        var used = false;
        var second = 99;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFWave2Hero");
            var foe = Spawn("CFWave2Foe", 2);
            Options(hero).Prey = foe;
            Options(hero).PreyPrecision = true;
            Strikes.PerformStrike(hero, foe, Bow(), hero, melee: false, countMap: false);
            used = Options(hero).PreyHitThisTurn;
            var first = Hurt(foe);
            Strikes.PerformStrike(hero, foe, Bow(), hero, melee: false, countMap: false);
            second = Hurt(foe) - first;
        });

        Assert.Multiple(() =>
        {
            Assert.That(used, Is.True, "The first hit on the prey counts, even from a bow.");
            Assert.That(second, Is.LessThanOrEqualTo(2), "Only the first hit each turn.");
        });
    }

    [Test]
    public async Task SneakAttackWorksAtRangeOnAnOffGuardTarget()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        // Without Sneak Attack a shot deals at most 2, so anything above that is the extra die.
        int guarded = 0, offGuard = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFWave2Hero");
            Options(hero).SneakAttack = true;
            for (var i = 0; i < 10; i++)
            {
                var alert = Spawn("CFWave2Foe", 2);
                var caught = Spawn("CFWave2Foe", 2);
                SEntMan.System<CFConditionSystem>().SetOffGuardTo(caught, hero, 60f);
                Strikes.PerformStrike(hero, alert, Bow(), hero, melee: false, countMap: false);
                Strikes.PerformStrike(hero, caught, Bow(), hero, melee: false, countMap: false);
                guarded = Math.Max(guarded, Hurt(alert));
                offGuard = Math.Max(offGuard, Hurt(caught));
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(guarded, Is.LessThanOrEqualTo(2));
            Assert.That(offGuard, Is.GreaterThan(2));
        });
    }

    [TestCase("EdgePrecision", true, false, false)]
    [TestCase("EdgeFlurry", false, true, false)]
    [TestCase("EdgeOutwit", false, false, true)]
    public async Task HuntersEdgeGivesOnlyWhatWasChosen(string edge, bool precision, bool flurry, bool outwit)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFCombatOptionsComponent options = default!;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { Class = "Ranger" };
            build.ClassChoices.Add(edge);
            options = Options(Built(build));
        });

        Assert.Multiple(() =>
        {
            Assert.That(options.PreyPrecision, Is.EqualTo(precision));
            Assert.That(options.PreyFlurry, Is.EqualTo(flurry));
            Assert.That(options.PreyOutwit, Is.EqualTo(outwit));
        });
    }

    [Test]
    public async Task OutwitRaisesAcAgainstThePreyOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, prey = 0, other = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFWave2Hero");
            var foe = Spawn("CFWave2Foe", 1);
            var bystander = Spawn("CFWave2Foe", 1);
            Strikes.TryPreviewMelee(foe, hero, out var before);
            plain = before.ArmorClass;

            Options(hero).PreyOutwit = true;
            Options(hero).Prey = foe;
            Strikes.TryPreviewMelee(foe, hero, out var against);
            prey = against.ArmorClass;
            Strikes.TryPreviewMelee(bystander, hero, out var elsewhere);
            other = elsewhere.ArmorClass;
        });

        Assert.Multiple(() =>
        {
            Assert.That(prey, Is.EqualTo(plain + 1));
            Assert.That(other, Is.EqualTo(plain));
        });
    }

    [Test]
    public async Task BraveryArrivesAtLevelThree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var rankBefore = CFProficiencyRank.Legendary;
        var rankAfter = CFProficiencyRank.Untrained;
        int frightened = 0, frightenedOne = -1;
        CFDegree upgraded = default;
        await Server.WaitPost(() =>
        {
            var hero = Built(new CFCharacterBuildData { Class = "Fighter" });
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            rankBefore = CFDefense.SaveRank(Server.ProtoMan, combat, "Will");
            LevelTo(hero, 3);
            rankAfter = CFDefense.SaveRank(Server.ProtoMan, combat, "Will");

            var conditions = SEntMan.System<CFConditionSystem>();
            conditions.ApplyFrightened(hero, 1);
            frightenedOne = conditions.Frightened(hero);
            conditions.ApplyFrightened(hero, 3);
            frightened = conditions.Frightened(hero);
            upgraded = SEntMan.System<CFBonusSystem>().UpgradeSave(hero, CFDefenseType.Will, CFDegree.Success, new() { "Fear" });
        });

        Assert.Multiple(() =>
        {
            Assert.That(rankBefore, Is.LessThan(CFProficiencyRank.Expert));
            Assert.That(rankAfter, Is.EqualTo(CFProficiencyRank.Expert));
            Assert.That(frightenedOne, Is.Zero);
            Assert.That(frightened, Is.EqualTo(2));
            Assert.That(upgraded, Is.EqualTo(CFDegree.CriticalSuccess));
        });
    }

    [Test]
    public async Task WeaponMasteryComesAtLevelFiveForTheChosenGroup()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFProficiencyRank early = default, chosen = default, other = default;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { Class = "Fighter" };
            build.OptionPicks.Add(new CFOptionPick { Source = "class:Fighter:Fighter Weapon Mastery", Slot = 0, Value = "Sword" });
            var hero = Built(build);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var weapons = SEntMan.System<CFWeaponSystem>();
            weapons.TryStats(Spawn("CFLongsword"), out var sword);
            weapons.TryStats(Spawn("CFBattleAxe"), out var axe);
            early = weapons.Rank(combat, sword);
            LevelTo(hero, 5);
            chosen = weapons.Rank(combat, sword);
            other = weapons.Rank(combat, axe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(early, Is.LessThan(CFProficiencyRank.Master));
            Assert.That(chosen, Is.EqualTo(CFProficiencyRank.Master));
            Assert.That(other, Is.LessThan(CFProficiencyRank.Master));
        });
    }

    [Test]
    public async Task DreadStrikerAndBrutalBeating()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int calmAc = 0, scaredAc = 0, frightened = 0, plainFrightened = -1;
        await Server.WaitPost(() =>
        {
            var conditions = SEntMan.System<CFConditionSystem>();
            var hero = Spawn("CFWave2Hero");
            var foe = Spawn("CFWave2Foe", 1);
            Learn(hero, "CFDreadStriker");
            Strikes.TryPreviewMelee(hero, foe, out var calm);
            calmAc = calm.ArmorClass;
            conditions.ApplyFrightened(foe, 1);
            Strikes.TryPreviewMelee(hero, foe, out var scared);

            // Frightened also lowers AC by its value, so the off-guard part is what is left after that.
            scaredAc = scared.ArmorClass + 1;

            var bruiser = Spawn("CFWave2Hero");
            Learn(bruiser, "CFBrutalBeating");
            for (var i = 0; i < 10; i++)
            {
                var victim = Spawn("CFWave2Foe", 1);
                Strikes.ReactionStrike(bruiser, victim);
                frightened = Math.Max(frightened, conditions.Frightened(victim));
            }

            var plain = Spawn("CFWave2Hero");
            var untouched = Spawn("CFWave2Foe", 1);
            Strikes.ReactionStrike(plain, untouched);
            plainFrightened = conditions.Frightened(untouched);
        });

        Assert.Multiple(() =>
        {
            Assert.That(scaredAc, Is.EqualTo(calmAc - CFFlanking.OffGuardAcPenalty));
            Assert.That(frightened, Is.EqualTo(1));
            Assert.That(plainFrightened, Is.Zero);
        });
    }

    [Test]
    public async Task MobilityMakesWalkingOutFree()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        float before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFWave2Hero");
            Options(hero).ReactiveStrike = true;
            var foe = Spawn("CFWave2Foe", 1);
            Learn(foe, "CFMobility");
            SEntMan.GetComponent<InputMoverComponent>(foe).HeldMoveButtons = MoveButtons.Walk;
            var physics = SEntMan.System<SharedPhysicsSystem>();
            physics.WakeBody(foe);
            physics.SetLinearVelocity(foe, new Vector2(5f, 0f));
            before = SEntMan.GetComponent<CFActionPoolComponent>(foe).Stamina;
            SEntMan.System<CFReactionSystem>().NoteLeftReach(hero, foe);
            after = SEntMan.GetComponent<CFActionPoolComponent>(foe).Stamina;
        });

        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public async Task DisruptPreyOnlyAnswersThePrey()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid prey = default, other = default;
        await Server.WaitPost(() =>
        {
            var physics = SEntMan.System<SharedPhysicsSystem>();
            var hero = Spawn("CFWave2Hero");
            Learn(hero, "CFDisruptPrey");
            prey = Spawn("CFWave2Foe", 1);
            other = Spawn("CFWave2Foe", 1);
            Options(hero).Prey = prey;
            physics.WakeBody(other);
            physics.SetLinearVelocity(other, new Vector2(5f, 0f));
            SEntMan.System<CFReactionSystem>().NoteLeftReach(hero, other);
            physics.WakeBody(prey);
            physics.SetLinearVelocity(prey, new Vector2(5f, 0f));
            SEntMan.System<CFReactionSystem>().NoteLeftReach(hero, prey);
        });

        await Pair.RunTicksSync(2);

        int preyHurt = 0, otherHurt = -1;
        await Server.WaitPost(() =>
        {
            preyHurt = Hurt(prey);
            otherHurt = Hurt(other);
        });

        Assert.Multiple(() =>
        {
            Assert.That(preyHurt, Is.GreaterThan(0));
            Assert.That(otherHurt, Is.Zero);
        });
    }

    [Test]
    public async Task EveryWaveTwoOptionWorksOrSaysTba()
    {
        var unmarked = new List<string>();
        await Server.WaitPost(() =>
        {
            var loc = Server.ResolveDependency<ILocalizationManager>();
            string[] classes = { "Fighter", "Rogue", "Ranger" };
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(feat => feat.Class is { } id && classes.Contains(id)))
            {
                var text = loc.GetString(feat.Description);
                var works = feat.Grants.Count > 0 || feat.Choices.Count > 0 || feat.TrainedSkills.Count > 0 || feat.Lores.Count > 0;
                if (!works && !text.Contains("TBA") && !text.Contains("implemented later") && !text.Contains("ot applied in play"))
                    unmarked.Add(feat.ID);
            }

            foreach (var id in classes)
            {
                foreach (var feature in Server.ProtoMan.Index<CFClassPrototype>(id).Features)
                {
                    if (feature.Description.Contains("implemented later"))
                        unmarked.Add($"{id}: {feature.Name}");
                }
            }
        });

        Assert.That(unmarked, Is.Empty);
    }
}
