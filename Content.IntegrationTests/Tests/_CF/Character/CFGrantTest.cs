using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Overlays;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>What an option grants reaches the character, and the rolls that are meant to read it.</summary>
[TestFixture]
[TestOf(typeof(CFCharacterFeatureSystem))]
public sealed class CFGrantTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    /// <summary>Everything the two switch statements in CFCharacterSpawnSystem used to do for a class.</summary>
    private static readonly Dictionary<string, (Func<CFCombatOptionsComponent, bool> Options, string[] Actions)> Classes = new()
    {
        ["Rogue"] = (o => o.SneakAttack, Array.Empty<string>()),
        ["Barbarian"] = (o => o.CanRage, new[] { "CFActRage" }),
        ["Fighter"] = (o => o.ReactiveStrike && o.ShieldBlock, Array.Empty<string>()),
        ["Monk"] = (o => o.Flurry && o.PowerfulFist, new[] { "CFActFlurry" }),
        ["Ranger"] = (o => o.PointMark == CFPointMark.Prey, Array.Empty<string>()),
        ["Swashbuckler"] = (o => o.PanacheStyle, Array.Empty<string>()),
        ["Magus"] = (o => o.Spellstrike, new[] { "CFActSpellstrike" }),
        ["Investigator"] = (o => o.Devise && o.PointMark == CFPointMark.Stratagem, Array.Empty<string>()),
        ["Thaumaturge"] = (o => o.Exploit && o.PointMark == CFPointMark.Exploit, Array.Empty<string>()),
        ["Bard"] = (o => o.Anthem, new[] { "CFActAnthem" }),
        ["Champion"] = (o => o.Champion && o.ShieldBlock, Array.Empty<string>()),
    };

    /// <summary>The same for a feat.</summary>
    private static readonly Dictionary<string, (Func<CFCombatOptionsComponent, bool> Options, string? Action)> Feats = new()
    {
        ["CFDoubleSlice"] = (o => o.DoubleSlice, null),
        ["CFExactingStrike"] = (o => o.ExactingStrike, null),
        ["CFSuddenChargeFighter"] = (_ => true, "CFActSuddenCharge"),
        ["CFSuddenChargeBarbarian"] = (_ => true, "CFActSuddenCharge"),
        ["CFSnaggingStrike"] = (o => o.SnaggingStrike, null),
        ["CFViciousSwing"] = (o => o.ViciousSwing, null),
        ["CFPointBlankStance"] = (_ => true, "CFActPointBlank"),
        ["CFMountainStance"] = (_ => true, "CFActMountainStance"),
        ["CFCraneStance"] = (_ => true, "CFActCraneStance"),
        ["CFDragonStance"] = (_ => true, "CFActDragonStance"),
        ["CFTigerStance"] = (_ => true, "CFActTigerStance"),
        ["CFReactiveShieldFighter"] = (o => o.ReactiveShield, null),
        ["CFReactiveShieldGuardian"] = (o => o.ReactiveShield, null),
        ["CFTwinFeint"] = (o => o.TwinFeint, null),
        ["CFYoureNextRogue"] = (o => o.YoureNext, null),
        ["CFYoureNextSwashbuckler"] = (o => o.YoureNext, null),
        ["CFNimbleDodge"] = (o => o.NimbleDodge, null),
        ["CFTumbleBehindRogue"] = (o => o.TumbleBehind, null),
        ["CFTumbleBehindSwashbuckler"] = (o => o.TumbleBehind, null),
    };

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFGrantHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 20
    currentHp: 20
    armorFromEquipment: true
    classId: CFGrantTestClass
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 1
      Dexterity: 0
      Constitution: 2
      Intelligence: 0
      Wisdom: 1
      Charisma: 3
  - type: CFExperience

- type: entity
  parent: CFGrantHero
  id: CFGrantFighter
  components:
  - type: CFCombatant
    classId: Fighter

- type: entity
  parent: CFGrantHero
  id: CFGrantWizard
  components:
  - type: CFCombatant
    classId: Wizard

- type: entity
  parent: CFLongsword
  id: CFGrantAdvancedBlade
  components:
  - type: CFWeapon
    category: Advanced
    damageType: Slashing
  - type: CFTrait
    traits:
    - Dwarf

- type: cfClass
  id: CFGrantTestClass
  name: cf-class-fighter
  description: cf-class-description-fighter
  hitPoints: 8
  keyAbility: Charisma
  hitPointsAbility: Constitution
  classDC: cf-class-fighter
  features:
  - level: 1
    name: First
    grants:
    - !type:CFGrantOption
      option: SneakAttack
  - level: 3
    name: Third
    grants:
    - !type:CFGrantProficiency
      proficiencies:
      - Fortitude
      - Perception
      - Unarmored
      - ClassDC
      - Spellcasting
      - Athletics
      rank: Expert

- type: cfFeat
  id: CFGrantTestBonuses
  name: cf-feat-double-slice
  description: cf-feat-double-slice-desc
  grants:
  - !type:CFGrantBonus
    stat: Saves
    type: Circumstance
    value: 2
    trait: Fear
  - !type:CFGrantBonus
    stat: Will
    type: Status
    value: 1
  - !type:CFGrantBonus
    stat: SkillChecks
    type: Circumstance
    value: 3
    skill: Intimidation
  - !type:CFGrantItem
    item: CFHealingPotionMinor

- type: cfFeat
  id: CFGrantTestSave
  name: cf-feat-double-slice
  description: cf-feat-double-slice-desc
  grants:
  - !type:CFGrantSaveUpgrade
    save: Will
    trait: Fear

- type: cfFeat
  id: CFGrantTestResistance
  name: cf-feat-double-slice
  description: cf-feat-double-slice-desc
  grants:
  - !type:CFGrantResistance
    damageType: Fire
    halfLevel: true
  - !type:CFGrantResistance
    damageType: Cold
    amount: 3

- type: cfFeat
  id: CFGrantTestSenses
  name: cf-feat-double-slice
  description: cf-feat-double-slice-desc
  grants:
  - !type:CFGrantSense
    sense: Scent
    rangeFeet: 15
  - !type:CFGrantSense
    sense: Darkvision
  - !type:CFGrantSense
    sense: LowLightVision
  - !type:CFGrantSpell
    spell: Shield
  - !type:CFGrantSpell
    spell: Shield
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private CFCombatantComponent Combat(EntityUid mob) => SEntMan.GetComponent<CFCombatantComponent>(mob);

    private CFAbilityScoreComponent Abilities(EntityUid mob) => SEntMan.GetComponent<CFAbilityScoreComponent>(mob);

    private EntityUid Spawn(string id, int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private void Grant(EntityUid mob, string feat)
    {
        Features.Apply(mob, Server.ProtoMan.Index<CFFeatPrototype>(feat).Grants, $"feat:{feat}");
    }

    private int Save(EntityUid mob, CFDefenseType save)
    {
        return CFDefense.SaveModifier(Server.ProtoMan, Combat(mob), Abilities(mob), save);
    }

    private List<string?> ActionIds(EntityUid mob)
    {
        return SEntMan.System<SharedActionsSystem>()
            .GetActions(mob)
            .Select(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID)
            .ToList();
    }

    private int Potions(EntityUid mob)
    {
        var lookup = SEntMan.System<EntityLookupSystem>();
        return lookup.GetEntitiesInRange(mob, 1.5f)
            .Count(item => SEntMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == "CFHealingPotionMinor");
    }

    private EntityUid Build(string classId, int x)
    {
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(x, 0));
        var build = new CFCharacterBuildData { Class = classId };
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));
        return hero;
    }

    [Test]
    public async Task EachClassKeepsWhatItHadBeforeGrants()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            foreach (var (classId, expected) in Classes)
            {
                var hero = Build(classId, 0);
                Assert.That(expected.Options(SEntMan.GetComponent<CFCombatOptionsComponent>(hero)), classId);
                Assert.That(ActionIds(hero), Is.SupersetOf(expected.Actions), classId);
                SEntMan.DeleteEntity(hero);
            }

            var alchemist = Build("Alchemist", 2);
            Assert.That(Potions(alchemist), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task EachFeatKeepsWhatItHadBeforeGrants()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var spawn = SEntMan.System<CFCharacterSpawnSystem>();
            foreach (var (featId, expected) in Feats)
            {
                var hero = Spawn("CFGrantHero");
                spawn.ApplyFeat(hero, Server.ProtoMan.Index<CFFeatPrototype>(featId));

                Assert.That(expected.Options(SEntMan.GetComponent<CFCombatOptionsComponent>(hero)), featId);
                if (expected.Action != null)
                    Assert.That(ActionIds(hero), Does.Contain(expected.Action), featId);

                SEntMan.DeleteEntity(hero);
            }
        });
    }

    [Test]
    public async Task ABackgroundCanStillGrantBattleMedicine()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var background = Server.ProtoMan.EnumeratePrototypes<CFBackgroundPrototype>().First(proto => proto.Grants.Count > 0);
            var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
            var build = new CFCharacterBuildData { Background = background.ID };
            SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));

            Assert.That(SEntMan.GetComponent<CFCombatOptionsComponent>(hero).BattleMedicine);
        });
    }

    [Test]
    public async Task AnOptionOnlyRunsOnce()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFGrantHero");
            Grant(hero, "CFGrantTestBonuses");
            Grant(hero, "CFGrantTestBonuses");

            Assert.That(Potions(hero), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ClassFeaturesWaitForTheirLevel()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFGrantHero");
            var combat = Combat(hero);
            var experience = SEntMan.System<CFExperienceSystem>();

            experience.AddXp(hero, CFExperienceComponent.XpPerLevel);
            Assert.That(combat.Level, Is.EqualTo(2));
            Assert.That(SEntMan.GetComponent<CFCombatOptionsComponent>(hero).SneakAttack, "Level 1 features count too.");
            Assert.That(combat.ProficiencyRanks, Is.Empty, "The level 3 feature has not been reached.");

            experience.AddXp(hero, CFExperienceComponent.XpPerLevel);
            Assert.That(combat.Level, Is.EqualTo(3));
            Assert.That(CFDefense.SaveRank(Server.ProtoMan, combat, "Fortitude"), Is.EqualTo(CFProficiencyRank.Expert));
        });
    }

    /// <summary>A Fighter is expert in Fortitude, Reflex, and martial weapons from level 1, and rolls like it.</summary>
    [Test]
    public async Task RollsUseTheRealRankAndNotJustTrained()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var fighter = Spawn("CFGrantFighter");
            var wizard = Spawn("CFGrantWizard", 1);
            var strikes = SEntMan.System<CFStrikeSystem>();

            Assert.That(Save(fighter, CFDefenseType.Fortitude), Is.EqualTo(2 + 1 + 4), "Expert");
            Assert.That(Save(fighter, CFDefenseType.Will), Is.EqualTo(1 + 1 + 2), "Trained");
            Assert.That(strikes.TryPreviewMelee(fighter, wizard, out var fists));
            Assert.That(fists.Attack, Is.EqualTo(1 + 1 + 4), "A Fighter is expert with fists.");
            Assert.That(strikes.TryPreviewMelee(wizard, fighter, out var weakFists));
            Assert.That(weakFists.Attack, Is.EqualTo(1 + 1 + 2));
        });
    }

    [Test]
    public async Task AGrantedRankBeatsAWorseOneAndNeverLowersOne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFGrantHero");
            var combat = Combat(hero);
            var armor = SEntMan.System<CFArmorSystem>();
            armor.RefreshArmorClass(hero);
            var unarmored = combat.ArmorClass;
            var classDc = CFCasting.ClassDc(Server.ProtoMan, combat, Abilities(hero)).Dc;
            var spellDc = CFCasting.Spellcasting(Server.ProtoMan, combat, Abilities(hero)).Dc;
            var perception = CFDefense.PerceptionModifier(Server.ProtoMan, combat, Abilities(hero));
            var fortitude = Save(hero, CFDefenseType.Fortitude);

            Features.ApplyClassFeatures(hero, Server.ProtoMan.Index<CFClassPrototype>("CFGrantTestClass"), 3);
            armor.RefreshArmorClass(hero);

            Assert.Multiple(() =>
            {
                Assert.That(combat.ArmorClass, Is.EqualTo(unarmored + 1 + 4), "Untrained to expert.");
                Assert.That(Save(hero, CFDefenseType.Fortitude), Is.EqualTo(fortitude + 1 + 4), "Untrained to expert.");
                Assert.That(CFDefense.PerceptionModifier(Server.ProtoMan, combat, Abilities(hero)), Is.EqualTo(perception + 1 + 4));
                Assert.That(CFCasting.ClassDc(Server.ProtoMan, combat, Abilities(hero)).Dc, Is.EqualTo(classDc + 2), "Trained to expert.");
                Assert.That(CFCasting.Spellcasting(Server.ProtoMan, combat, Abilities(hero)).Dc, Is.EqualTo(spellDc + 2));
                Assert.That(CFDefense.SkillRank(Server.ProtoMan, combat, "Athletics"), Is.EqualTo(CFProficiencyRank.Expert));
            });

            Features.RaiseProficiency(hero, "Fortitude", CFProficiencyRank.Trained);
            Assert.That(CFDefense.SaveRank(Server.ProtoMan, combat, "Fortitude"), Is.EqualTo(CFProficiencyRank.Expert));
        });
    }

    [Test]
    public async Task ABonusCountsOnlyForWhatItNames()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFGrantHero");
            var foe = Spawn("CFGrantHero", 1);
            var conditions = SEntMan.System<CFConditionSystem>();
            Assert.That(conditions.TryGetDemoralizeCheck(hero, foe, out var plainSkill, out var plainDc));

            Grant(hero, "CFGrantTestBonuses");
            Grant(foe, "CFGrantTestBonuses");

            Assert.Multiple(() =>
            {
                var fear = new HashSet<ProtoId<CFTraitPrototype>> { "Fear" };
                Assert.That(Bonuses.Total(hero, CFBonusStat.Saves, CFBonusStat.Will, fear), Is.EqualTo(3), "Different types add up.");
                Assert.That(Bonuses.Total(hero, CFBonusStat.Saves, CFBonusStat.Will), Is.EqualTo(1), "Not against fear.");
                Assert.That(Bonuses.Total(hero, CFBonusStat.Saves, CFBonusStat.Reflex), Is.Zero, "Will only.");
                Assert.That(conditions.SkillBonus(hero, "Intimidation"), Is.EqualTo(3));
                Assert.That(conditions.SkillBonus(hero, "Athletics"), Is.Zero);

                Assert.That(conditions.TryGetDemoralizeCheck(hero, foe, out var skill, out var dc));
                Assert.That(skill, Is.EqualTo(plainSkill + 3), "The skill bonus reaches a real check.");
                Assert.That(dc, Is.EqualTo(plainDc + 3), "Demoralize is a fear effect, so both of the foe's bonuses raise its DC.");
            });
        });
    }

    [Test]
    public async Task TwoUntypedBonusesBothCountAndATimedOneDoesNotMergeWithALastingOne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFGrantHero");
            Bonuses.AddPermanentBonus(hero, CFBonusStat.Perception, CFBonusType.Untyped, 1);
            Bonuses.AddPermanentBonus(hero, CFBonusStat.Perception, CFBonusType.Untyped, 1);
            Bonuses.AddBonus(hero, CFBonusStat.Perception, CFBonusType.Untyped, 1, 60f);

            Assert.That(Bonuses.Total(hero, CFBonusStat.Perception), Is.EqualTo(3));
        });
    }

    /// <summary>
    /// At a DC of the modifier minus 7, a plain Success only comes from a 2 on the die. With the upgrade it never comes.
    /// Without it, 400 rolls miss that 2 about once in 800,000 runs, so this does not fail by luck.
    /// </summary>
    [Test]
    public async Task ASaveUpgradeChangesTheRollAgainstItsTraitOnly()
    {
        const int rolls = 400;
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFGrantHero");
            var conditions = SEntMan.System<CFConditionSystem>();
            var fear = new HashSet<ProtoId<CFTraitPrototype>> { "Fear" };
            var poison = new HashSet<ProtoId<CFTraitPrototype>> { "Poison" };
            var dc = Save(hero, CFDefenseType.Will) - 7;
            Grant(hero, "CFGrantTestSave");

            int Successes(CFDefenseType save, HashSet<ProtoId<CFTraitPrototype>> traits)
            {
                var plain = 0;
                for (var i = 0; i < rolls; i++)
                {
                    if (conditions.RollDefense(hero, save, dc, out _, traits) == CFDegree.Success)
                        plain++;
                }

                return plain;
            }

            Assert.That(Successes(CFDefenseType.Will, fear), Is.Zero);
            Assert.That(Successes(CFDefenseType.Will, poison), Is.GreaterThan(0), "Another trait gets no upgrade.");
            Assert.That(Bonuses.UpgradeSave(hero, CFDefenseType.Fortitude, CFDegree.Success, fear), Is.EqualTo(CFDegree.Success), "Another save gets none.");
            Assert.That(Bonuses.UpgradeSave(hero, CFDefenseType.Will, CFDegree.Failure, fear), Is.EqualTo(CFDegree.Failure), "Only a success improves.");
        });
    }

    [Test]
    public async Task ASourceTellsTheCheckItsTraits()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFGrantHero");
            var spell = Spawn("CFFear");
            var args = SEntMan.System<CFEffectSystem>().BuildArgs(spell, hero, hero, null, null, out _);

            Assert.That(args.Traits, Does.Contain(new ProtoId<CFTraitPrototype>("Fear")));
        });
    }

    [Test]
    public async Task AHalfLevelResistanceGrowsAndATimedOneLeavesALastingOneAlone()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitAssertion(() =>
        {
            hero = Spawn("CFGrantHero");
            Grant(hero, "CFGrantTestResistance");

            Assert.That(Bonuses.ModifyDamage(hero, "Fire", 10), Is.EqualTo(9), "At least 1 at level 1.");
            Combat(hero).Level = 4;
            Assert.That(Bonuses.ModifyDamage(hero, "Fire", 10), Is.EqualTo(8));

            Assert.That(Bonuses.ModifyDamage(hero, "Cold", 10), Is.EqualTo(7));
            Bonuses.AddResistance(hero, "Cold", 5, weakness: false, seconds: 1f);
            Assert.That(Bonuses.ModifyDamage(hero, "Cold", 10), Is.EqualTo(5), "The highest applies.");
        });

        await Server.WaitRunTicks(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Bonuses.ModifyDamage(hero, "Cold", 10), Is.EqualTo(7), "The spell ran out. The feat did not.");
        });
    }

    [Test]
    public async Task AFamiliarWeaponCountsAsSimpler()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.System<CFWeaponSystem>();
            var wizard = Combat(Spawn("CFGrantWizard"));
            var fighter = Combat(Spawn("CFGrantFighter"));
            var longsword = Spawn("CFLongsword", 1);
            var blade = Spawn("CFGrantAdvancedBlade", 1);
            Assert.That(weapons.TryStats(longsword, out var sword));
            Assert.That(weapons.TryStats(blade, out var advanced));

            Assert.That(weapons.Rank(wizard, sword), Is.EqualTo(CFProficiencyRank.Untrained));
            Assert.That(weapons.Rank(fighter, advanced), Is.EqualTo(CFProficiencyRank.Trained));

            wizard.FamiliarWeapons.Add("CFLongsword");
            fighter.FamiliarWeaponTraits.Add("Dwarf");

            Assert.That(weapons.Rank(wizard, sword), Is.EqualTo(CFProficiencyRank.Trained), "A listed weapon is simple.");
            Assert.That(weapons.Rank(fighter, advanced), Is.EqualTo(CFProficiencyRank.Expert), "Advanced counts as martial.");
        });
    }

    [Test]
    public async Task AGroupRankCountsForThatGroupOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var weapons = SEntMan.System<CFWeaponSystem>();
            var wizard = Spawn("CFGrantWizard");
            Assert.That(weapons.TryStats(Spawn("CFLongsword", 1), out var sword));

            Features.RaiseProficiency(wizard, "Bow", CFProficiencyRank.Expert);
            Assert.That(weapons.Rank(Combat(wizard), sword), Is.EqualTo(CFProficiencyRank.Untrained));

            Features.RaiseProficiency(wizard, "Sword", CFProficiencyRank.Expert);
            Assert.That(weapons.Rank(Combat(wizard), sword), Is.EqualTo(CFProficiencyRank.Expert));
        });
    }

    [Test]
    public async Task SensesAndSpellsReachTheCharacter()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFGrantHero");
            var sneaker = Spawn("CFGrantHero", 2);
            var stealth = SEntMan.EnsureComponent<CFStealthComponent>(sneaker);
            stealth.Active = true;
            stealth.Detection[hero] = CFDetection.Undetected;
            var detection = SEntMan.System<CFStealthSystem>();
            Assert.That(detection.Detection(sneaker, hero), Is.EqualTo(CFDetection.Undetected));

            Grant(hero, "CFGrantTestSenses");

            Assert.Multiple(() =>
            {
                Assert.That(detection.Detection(sneaker, hero), Is.EqualTo(CFDetection.Hidden), "Scent finds its square, 10 feet away.");
                Assert.That(SEntMan.GetComponent<NightVisionComponent>(hero).LightingColor.A, Is.GreaterThan(0.15f), "Low-light does not dim darkvision.");
                Assert.That(ActionIds(hero).Count(id => id == "CFShield"), Is.EqualTo(1), "Two grants of one spell give one action.");
                Assert.That(Features.GrantSpell(hero, "NoSuchSpell"), Is.False);
            });

            SEntMan.GetComponent<CFSensesComponent>(hero).ImpreciseFeet = 5;
            Assert.That(detection.Detection(sneaker, hero), Is.EqualTo(CFDetection.Undetected), "Out of range.");
        });
    }
}
