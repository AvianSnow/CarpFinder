using System.Collections.Generic;
using System.Linq;
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

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Rank math. Plain, so no game runs.</summary>
[TestFixture]
public sealed class CFSkillRankTest
{
    [TestCase(CFProficiencyRank.Untrained, 1, CFProficiencyRank.Trained)]
    [TestCase(CFProficiencyRank.Trained, 1, CFProficiencyRank.Expert)]
    [TestCase(CFProficiencyRank.Untrained, 2, CFProficiencyRank.Expert)]
    [TestCase(CFProficiencyRank.Trained, 2, CFProficiencyRank.Expert)]
    [TestCase(CFProficiencyRank.Expert, 1, CFProficiencyRank.Expert)]
    public void AnIncreaseRaisesOneRankUpToExpert(CFProficiencyRank rank, int increases, CFProficiencyRank expected)
    {
        Assert.That(CFSkills.RaiseRank(rank, increases), Is.EqualTo(expected));
    }
}

/// <summary>Choices made in the lobby reach the character at the level they were picked for, and not before.</summary>
[TestFixture]
[TestOf(typeof(CFExperienceSystem))]
public sealed class CFLevelChoicesTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFLevelChoicesHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 10
    currentHp: 10
    trainedSkills:
    - Athletics
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 4
      Constitution: 1
      Dexterity: 0
  - type: CFExperience
";

    private CFDevMap _map = default!;

    private CFExperienceSystem Experience => SEntMan.System<CFExperienceSystem>();

    private EntityUid Hero()
    {
        return SEntMan.SpawnEntity("CFLevelChoicesHero", _map.Local(0, 0));
    }

    private void LevelTo(EntityUid hero, int level)
    {
        var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
        Experience.AddXp(hero, (level - combat.Level) * CFExperienceComponent.XpPerLevel);
        Assert.That(combat.Level, Is.EqualTo(level));
    }

    private CFProficiencyRank Rank(EntityUid hero, string skill)
    {
        return CFDefense.SkillRank(Server.ProtoMan, SEntMan.GetComponent<CFCombatantComponent>(hero), skill);
    }

    private CFFeatPrototype ExpertFeat()
    {
        return Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>()
            .First(feat => feat.RequiresSkill != null && feat.RequiresRank == CFProficiencyRank.Expert && feat.Level <= 4);
    }

    /// <summary>Asserts on this thread, so a failure keeps its message.</summary>
    [Test]
    public async Task TheSlotsHaveFeatsToChooseFrom()
    {
        List<CFFeatPrototype> feats = default!;
        var unknown = new List<string>();
        await Server.WaitPost(() =>
        {
            feats = Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().ToList();
            foreach (var feat in feats.Where(feat => feat.RequiresSkill != null))
            {
                if (!Server.ProtoMan.HasIndex(feat.RequiresSkill!.Value))
                    unknown.Add($"{feat.ID} needs {feat.RequiresSkill}");
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(unknown, Is.Empty);
            Assert.That(feats.Count(feat => feat.FitsSkillSlot(2)), Is.GreaterThan(30));
            Assert.That(feats.Count(feat => feat.FitsGeneralSlot(3) && feat.Category == CFFeatCategory.General), Is.GreaterThan(5));
            Assert.That(feats.Any(feat => feat.FitsSkillSlot(2) && feat.Level == 2), "Level 2 feats are on offer as well as level 1.");
            Assert.That(feats.Any(feat => feat.Category == CFFeatCategory.General && feat.FitsSkillSlot(4)), Is.False);
        });
    }

    [Test]
    public async Task ASkillIncreaseWaitsForItsLevel()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            xp.SkillIncreaseLevel3 = "Athletics";
            xp.SkillIncreaseLevel5 = "Stealth";

            LevelTo(hero, 2);
            Assert.That(Rank(hero, "Athletics"), Is.EqualTo(CFProficiencyRank.Trained), "Level 3 has not come yet.");

            LevelTo(hero, 3);
            Assert.That(Rank(hero, "Athletics"), Is.EqualTo(CFProficiencyRank.Expert));
            Assert.That(Rank(hero, "Stealth"), Is.EqualTo(CFProficiencyRank.Untrained));

            LevelTo(hero, 5);
            Assert.That(Rank(hero, "Stealth"), Is.EqualTo(CFProficiencyRank.Trained));
        });
    }

    [Test]
    public async Task LevelFiveBoostsLandOnceAndPayBackConstitution()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(hero);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            xp.AbilityBoostsLevel5 = new() { CFAbility.Strength, CFAbility.Constitution, CFAbility.Dexterity, CFAbility.Wisdom };

            LevelTo(hero, 4);
            Assert.That(abilities.GetModifier(CFAbility.Constitution), Is.EqualTo(1), "Level 5 has not come yet.");
            var before = combat.MaxHp;

            LevelTo(hero, 5);
            Assert.Multiple(() =>
            {
                Assert.That(abilities.GetModifier(CFAbility.Strength), Is.EqualTo(4), "At +4 a boost is only half of one.");
                Assert.That(abilities.GetModifier(CFAbility.Constitution), Is.EqualTo(2));
                Assert.That(abilities.GetModifier(CFAbility.Dexterity), Is.EqualTo(1));
                Assert.That(abilities.GetModifier(CFAbility.Wisdom), Is.EqualTo(1));
                Assert.That(combat.MaxHp - before, Is.EqualTo(1 + 4), "This level, plus 1 for each of the four before it.");
            });
        });
    }

    [Test]
    public async Task AFeatNeedsItsRankByThatLevel()
    {
        await Server.WaitAssertion(() =>
        {
            var feat = ExpertFeat();
            var skill = feat.RequiresSkill!.Value;
            var build = new CFCharacterBuildData { SkillIncreaseLevel3 = skill };
            build.ClassTrainedSkills.Add(skill);

            Assert.Multiple(() =>
            {
                Assert.That(CFSkills.Qualifies(Server.ProtoMan, build, null, feat, 2), Is.False, "Expert comes at level 3.");
                Assert.That(CFSkills.Qualifies(Server.ProtoMan, build, null, feat, 4));
            });
        });
    }

    [Test]
    public async Task APickTheBuildCannotHaveIsDroppedAtSpawn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var plain = Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>()
                .First(feat => feat.FitsSkillSlot(2) && feat.RequiresSkill == null);
            var general = Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>()
                .First(feat => feat.Category == CFFeatCategory.General);
            var build = new CFCharacterBuildData
            {
                SkillFeatLevel2 = ExpertFeat().ID,
                SkillFeatLevel4 = general.ID,
                GeneralFeatLevel3 = plain.ID,
                SkillIncreaseLevel5 = "NoSuchSkill",
            };
            build.AbilityBoostsLevel5.AddRange(new[]
            {
                CFAbility.Strength, CFAbility.Strength, CFAbility.Dexterity, CFAbility.Wisdom, CFAbility.Charisma, CFAbility.Constitution,
            });

            var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
            SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));

            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            Assert.Multiple(() =>
            {
                Assert.That(xp.SkillFeatLevel2, Is.Null, "It is not expert in that skill by level 2.");
                Assert.That(xp.SkillFeatLevel4, Is.Null, "A general feat does not fit a skill feat slot.");
                Assert.That(xp.GeneralFeatLevel3, Is.EqualTo(plain.ID), "A skill feat fits a general feat slot.");
                Assert.That(xp.SkillIncreaseLevel5, Is.Null);
                Assert.That(xp.AbilityBoostsLevel5, Is.EqualTo(new[]
                {
                    CFAbility.Strength, CFAbility.Dexterity, CFAbility.Wisdom, CFAbility.Charisma,
                }), "Four, all different.");
                Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).Level, Is.EqualTo(1), "Everyone starts at level 1.");
            });
        });
    }

    [Test]
    public void TheNewChoicesSurviveACopy()
    {
        var build = new CFCharacterBuildData { SkillFeatLevel2 = "CFAssurance", SkillIncreaseLevel3 = "Athletics" };
        build.AbilityBoostsLevel5.Add(CFAbility.Strength);

        var copy = build.Clone();
        Assert.That(copy.Equals(build));

        copy.AbilityBoostsLevel5.Add(CFAbility.Wisdom);
        Assert.That(copy.Equals(build), Is.False, "A changed copy counts as changed, or the Save button would not light up.");
    }
}
