using System.Collections.Generic;
using System.Linq;
using Content.Shared.CF.Character.Grants;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Thief racket's damage rule. Plain math, so no game runs.</summary>
[TestFixture]
public sealed class CFFinesseDamageTest
{
    private static CFAbilityScoreComponent Abilities(int strength, int dexterity)
    {
        return new CFAbilityScoreComponent
        {
            AbilityModifiers = new Dictionary<CFAbility, int>
            {
                [CFAbility.Strength] = strength,
                [CFAbility.Dexterity] = dexterity,
            },
        };
    }

    private static CFStrikeWeaponStats Weapon(bool finesse, bool ranged = false, bool thrown = false)
    {
        return new CFStrikeWeaponStats(1, 6, "Piercing", "SimpleWeapons", new CFWeaponTraits { Finesse = finesse }, Ranged: ranged, ThrownStrike: thrown);
    }

    [TestCase(1, 4, true, false, false, true, ExpectedResult = 4, TestName = "Thief adds Dexterity with finesse")]
    [TestCase(4, 1, true, false, false, true, ExpectedResult = 4, TestName = "Thief keeps a higher Strength")]
    [TestCase(1, 4, false, false, false, true, ExpectedResult = 1, TestName = "Thief needs a finesse weapon")]
    [TestCase(1, 4, true, false, false, false, ExpectedResult = 1, TestName = "Others add Strength")]
    [TestCase(1, 4, true, true, true, true, ExpectedResult = 1, TestName = "A thrown finesse weapon is not melee")]
    public int DamageAttribute(int strength, int dexterity, bool finesse, bool ranged, bool thrown, bool thief)
    {
        return CFMelee.DamageBonus(Abilities(strength, dexterity), Weapon(finesse, ranged, thrown), null, thief);
    }
}

/// <summary>Subclass and deity picks: what they give, and what happens to picks that do not fit.</summary>
[TestFixture]
[TestOf(typeof(CFClassChoices))]
public sealed class CFClassChoiceTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: cfClassChoice
  id: CFTestLaterChoice
  class: Rogue
  slot: cf-class-rogue
  name: cf-class-choice-thief
  description: cf-class-choice-thief-desc
  features:
  - level: 3
    name: Later
    grants:
    - !type:CFGrantOption
      option: Flurry

- type: cfDeity
  id: CFTestDeity
  name: cf-class-choice-thief
  description: cf-class-choice-thief-desc
  trainedSkills:
  - Crafting
  grants:
  - !type:CFGrantOption
    option: Flurry
";

    private EntityUid Spawn(CFDevMap map, CFCharacterBuildData build)
    {
        var hero = SEntMan.SpawnEntity("MobHuman", map.Local(0, 0));
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));
        return hero;
    }

    private static CFCharacterBuildData Rogue(params string[] choices)
    {
        var build = new CFCharacterBuildData { Class = "Rogue" };
        foreach (var choice in choices)
            build.ClassChoices.Add(choice);

        return build;
    }

    [Test]
    public async Task ThiefGetsItsSkillAndDamageRule()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var trained = new List<string>();
        var dexterity = 0;
        var finesse = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn(map, Rogue("Thief"));
            trained = SEntMan.GetComponent<CFCombatantComponent>(hero).TrainedSkills.ToList();
            dexterity = SEntMan.GetComponent<CFAbilityScoreComponent>(hero).GetModifier(CFAbility.Dexterity);
            finesse = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).FinesseDexterity;
        });

        Assert.Multiple(() =>
        {
            Assert.That(trained, Does.Contain("Thievery"));
            Assert.That(finesse, Is.True);
            Assert.That(dexterity, Is.EqualTo(1), "A Thief keeps the class's own key attribute.");
        });
    }

    [Test]
    public async Task RuffianChangesKeyAttributeAndArmor()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var trained = new List<string>();
        var strength = 0;
        var dexterity = 0;
        var classDc = 0;
        var mediumArmor = CFProficiencyRank.Untrained;
        var unchangedArmor = CFProficiencyRank.Untrained;
        await Server.WaitPost(() =>
        {
            var hero = Spawn(map, Rogue("Ruffian"));
            var plain = Spawn(map, Rogue());
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(hero);
            trained = combat.TrainedSkills.ToList();
            strength = abilities.GetModifier(CFAbility.Strength);
            dexterity = abilities.GetModifier(CFAbility.Dexterity);
            classDc = CFCasting.ClassDc(Server.ProtoMan, combat, abilities).Dc;
            mediumArmor = CFDefense.ArmorRank(Server.ProtoMan, combat, "MediumArmor");
            unchangedArmor = CFDefense.ArmorRank(Server.ProtoMan, SEntMan.GetComponent<CFCombatantComponent>(plain), "MediumArmor");
        });

        Assert.Multiple(() =>
        {
            Assert.That(trained, Does.Contain("Intimidation"));
            Assert.That(strength, Is.EqualTo(1), "The key attribute boost goes to Strength.");
            Assert.That(dexterity, Is.EqualTo(0), "And not to the class's usual Dexterity.");
            Assert.That(classDc, Is.EqualTo(14), "10 + Strength 1 + trained at level 1 (3).");
            Assert.That(mediumArmor, Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(unchangedArmor, Is.EqualTo(CFProficiencyRank.Untrained), "A Rogue with no racket has no medium armor.");
        });
    }

    [Test]
    public async Task PicksThatDoNotFitGiveNothing()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var wrongClassTrained = new List<string>();
        var wrongClassChoices = new List<string>();
        var repeatedChoices = new List<string>();
        var repeatedTrained = new List<string>();
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { Class = "Fighter" };
            build.ClassChoices.Add("Thief");
            var wrong = Spawn(map, build);
            wrongClassTrained = SEntMan.GetComponent<CFCombatantComponent>(wrong).TrainedSkills.ToList();
            wrongClassChoices = SEntMan.GetComponent<CFExperienceComponent>(wrong).ClassChoices.ToList();

            var repeated = Spawn(map, Rogue("Thief", "Ruffian", "NoSuchChoice"));
            repeatedChoices = SEntMan.GetComponent<CFExperienceComponent>(repeated).ClassChoices.ToList();
            repeatedTrained = SEntMan.GetComponent<CFCombatantComponent>(repeated).TrainedSkills.ToList();
        });

        Assert.Multiple(() =>
        {
            Assert.That(wrongClassTrained, Does.Not.Contain("Thievery"));
            Assert.That(wrongClassChoices, Is.Empty);
            Assert.That(repeatedChoices, Is.EqualTo(new[] { "Thief" }), "One option per slot, the first.");
            Assert.That(repeatedTrained, Does.Not.Contain("Intimidation"));
        });
    }

    [Test]
    public async Task ALaterFeatureWaitsForItsLevel()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var atFirst = true;
        var atTwo = true;
        var atThree = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn(map, Rogue("CFTestLaterChoice"));
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            var experience = SEntMan.System<CFExperienceSystem>();
            atFirst = options.Flurry;
            experience.AddXpAndLevelUp(hero, CFExperienceComponent.XpPerLevel);
            atTwo = options.Flurry;
            experience.AddXpAndLevelUp(hero, CFExperienceComponent.XpPerLevel);
            atThree = options.Flurry;
        });

        Assert.Multiple(() =>
        {
            Assert.That(atFirst, Is.False);
            Assert.That(atTwo, Is.False);
            Assert.That(atThree, Is.True);
        });
    }

    [TestCase("Solhara", "Heal")]
    [TestCase("Ombrix", "Harm")]
    [TestCase("Vaelis", "Heal,Harm")]
    public async Task ADeityDecidesWhatItsFontCasts(string deity, string spells)
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var spare = new List<string>();
        await Server.WaitPost(() =>
        {
            var cleric = Spawn(map, new CFCharacterBuildData { Class = "Cleric", Deity = deity });
            spare = SEntMan.GetComponent<CFSpellbookComponent>(cleric).SpareSlotSpells.Select(spell => spell.Id).ToList();
        });

        Assert.That(spare, Is.EquivalentTo(spells.Split(',')));
    }

    [Test]
    public async Task AChampionGetsItsDeitysSkillAndWeapon()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var trained = new List<string>();
        var weapons = new List<string>();
        await Server.WaitPost(() =>
        {
            var combat = SEntMan.GetComponent<CFCombatantComponent>(Spawn(map, new CFCharacterBuildData { Class = "Champion", Deity = "Morvane" }));
            trained = combat.TrainedSkills.ToList();
            weapons = combat.FamiliarWeapons.Select(weapon => weapon.ToString()).ToList();
        });

        Assert.Multiple(() =>
        {
            Assert.That(trained, Does.Contain("Religion"));
            Assert.That(weapons, Does.Contain("CFStaff"));
        });
    }

    [Test]
    public async Task EveryDeityGivesASkillAndAWeapon()
    {
        var incomplete = new List<string>();
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var deity in Server.ProtoMan.EnumeratePrototypes<CFDeityPrototype>().Where(deity => !deity.ID.Contains("Test")))
            {
                count++;
                if (deity.TrainedSkills.Count == 0 || !deity.Grants.OfType<CFGrantWeaponFamiliarity>().Any(grant => grant.Weapons.Count > 0))
                    incomplete.Add(deity.ID);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(incomplete, Is.Empty);
        });
    }

    [Test]
    public async Task ADeityOnlyCountsForAClassThatFollowsOne()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var clericTrained = new List<string>();
        var clericOption = false;
        var rogueTrained = new List<string>();
        var rogueOption = true;
        await Server.WaitPost(() =>
        {
            var cleric = Spawn(map, new CFCharacterBuildData { Class = "Cleric", Deity = "CFTestDeity" });
            clericTrained = SEntMan.GetComponent<CFCombatantComponent>(cleric).TrainedSkills.ToList();
            clericOption = SEntMan.GetComponent<CFCombatOptionsComponent>(cleric).Flurry;

            var rogue = Spawn(map, new CFCharacterBuildData { Class = "Rogue", Deity = "CFTestDeity" });
            rogueTrained = SEntMan.GetComponent<CFCombatantComponent>(rogue).TrainedSkills.ToList();
            rogueOption = SEntMan.GetComponent<CFCombatOptionsComponent>(rogue).Flurry;
        });

        Assert.Multiple(() =>
        {
            Assert.That(clericTrained, Does.Contain("Crafting"));
            Assert.That(clericOption, Is.True);
            Assert.That(rogueTrained, Does.Not.Contain("Crafting"));
            Assert.That(rogueOption, Is.False);
        });
    }

    [Test]
    public async Task SanitizeDropsWhatNoLongerFits()
    {
        CFCharacterBuildData build = default!;
        await Server.WaitPost(() =>
        {
            build = new CFCharacterBuildData { Class = "Fighter", Deity = "CFTestDeity" };
            build.ClassChoices.Add("Thief");
            build.ClassChoices.Add("NoSuchChoice");
            CFClassChoices.Sanitize(Server.ProtoMan, build);
        });

        Assert.Multiple(() =>
        {
            Assert.That(build.ClassChoices, Is.Empty);
            Assert.That(build.Deity, Is.Null);
        });
    }

    [Test]
    public async Task PickingARacketFreesAClassSkillPickItNowTrains()
    {
        CFCharacterBuildData build = default!;
        await Server.WaitPost(() =>
        {
            build = Rogue("Thief");
            build.ClassTrainedSkills.Add("Deception");
            build.ClassTrainedSkills.Add("Athletics");
            CFClassChoices.Assign(Server.ProtoMan, build, "Scoundrel");
        });

        Assert.Multiple(() =>
        {
            Assert.That(build.ClassChoices.Select(id => id.Id), Is.EqualTo(new[] { "Scoundrel" }), "The new racket replaces the old one.");
            Assert.That(build.ClassTrainedSkills.Select(id => id.Id), Is.EqualTo(new[] { "Athletics" }));
        });
    }

    /// <summary>Builds are stored as data nodes. One saved before subclasses existed has no such fields.</summary>
    [Test]
    public async Task ABuildSavedBeforeSubclassesStillLoads()
    {
        CFCharacterBuildData old = default!;
        CFCharacterBuildData loaded = default!;
        var saved = Rogue("Thief");
        saved.Deity = "CFTestDeity";
        await Server.WaitPost(() =>
        {
            var serialization = Server.ResolveDependency<ISerializationManager>();
            var node = new MappingDataNode();
            node.Add("class", new ValueDataNode("Rogue"));
            old = serialization.Read<CFCharacterBuildData>(node, notNullableOverride: true);

            var written = serialization.WriteValue(saved, alwaysWrite: true, notNullableOverride: true);
            loaded = serialization.Read<CFCharacterBuildData>((MappingDataNode) written, notNullableOverride: true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(old.Class?.Id, Is.EqualTo("Rogue"));
            Assert.That(old.ClassChoices, Is.Empty);
            Assert.That(old.Deity, Is.Null);
            Assert.That(loaded.Equals(saved), Is.True, "A pick survives a save and a load.");
        });
    }

    [Test]
    public void TheNewPicksSurviveACopy()
    {
        var build = Rogue("Thief");
        build.Deity = "CFTestDeity";

        var copy = build.Clone();
        Assert.That(copy.Equals(build));

        copy.ClassChoices.Add("Ruffian");
        Assert.That(copy.Equals(build), Is.False, "A changed copy counts as changed, or the Save button would not light up.");
        Assert.That(build.ClassChoices, Has.Count.EqualTo(1), "A copy has its own list.");
    }
}
