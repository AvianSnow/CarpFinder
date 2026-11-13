using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Preferences;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>
/// The lobby builds level 1. Each later level waits for the player to take it on the character sheet, with the picks
/// they made there, and the server reads only that level's picks.
/// </summary>
[TestFixture]
[TestOf(typeof(CFLevelUp))]
public sealed class CFLevelUpTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFLevelUpTestHero
  components:
  - type: CFCombatant
    level: 1
  - type: CFAbilityScore
";

    private CFDevMap _map = default!;

    private IPrototypeManager Protos => Server.ProtoMan;

    private EntityUid Hero(CFCharacterBuildData build)
    {
        var hero = SEntMan.SpawnEntity("CFLevelUpTestHero", _map.Local(0, 0));
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));
        return hero;
    }

    private int Level(EntityUid hero) => SEntMan.GetComponent<CFCombatantComponent>(hero).Level;

    [Test]
    public async Task XpWaitsForThePlayerToTakeTheLevel()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(new CFCharacterBuildData { Class = "Fighter" });
            var experience = SEntMan.System<CFExperienceSystem>();
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);

            Assert.That(experience.TryLevelUp(hero), Is.False, "No XP yet.");
            experience.AddXp(hero, CFExperienceComponent.XpPerLevel);
            Assert.Multiple(() =>
            {
                Assert.That(Level(hero), Is.EqualTo(1), "XP alone does not level.");
                Assert.That(xp.CanLevelUp(Level(hero)));
                Assert.That(xp.Build, Is.Not.Null, "The build is kept for the Level Up tab.");
            });

            Assert.That(experience.TryLevelUp(hero));
            Assert.Multiple(() =>
            {
                Assert.That(Level(hero), Is.EqualTo(2));
                Assert.That(xp.Xp, Is.Zero);
                Assert.That(xp.CanLevelUp(2), Is.False);
                Assert.That(xp.Build!.Level, Is.EqualTo(2));
            });
        });
    }

    [Test]
    public async Task TakingALevelReadsOnlyThatLevelsPicks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(new CFCharacterBuildData { Class = "Fighter" });
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            var sent = xp.Build!.Clone();

            var classFeat = CFLevelUp.FeatOptions(Protos, sent, xp.AncestryId, CFLevelSlot.ClassFeat, 2).First();
            var skillFeat = CFLevelUp.FeatOptions(Protos, sent, xp.AncestryId, CFLevelSlot.SkillFeat, 2).First();
            CFLevelUp.SetPick(sent, CFLevelSlot.ClassFeat, 2, classFeat.ID);
            CFLevelUp.SetPick(sent, CFLevelSlot.SkillFeat, 2, skillFeat.ID);
            sent.SkillIncreaseLevel3 = "Athletics";
            sent.ClassFeatLevel4 = classFeat.ID;

            var experience = SEntMan.System<CFExperienceSystem>();
            experience.AddXp(hero, CFExperienceComponent.XpPerLevel);
            Assert.That(experience.TryLevelUp(hero, sent));
            Assert.Multiple(() =>
            {
                Assert.That(xp.ClassFeatLevel2, Is.EqualTo(classFeat.ID));
                Assert.That(xp.SkillFeatLevel2, Is.EqualTo(skillFeat.ID));
                Assert.That(xp.Build!.SkillIncreaseLevel3, Is.Null, "Level 3's pick waits for level 3.");
                Assert.That(xp.Build.ClassFeatLevel4, Is.Null, "Level 4's too.");
                Assert.That(CFLevelUp.Unfilled(Protos, xp.Build, xp.AncestryId, 2, new Dictionary<CFAbility, int>()), Is.Zero);
            });
        });
    }

    /// <summary>
    /// A Sorcerer reached level 5 with an Intelligence boost but no language left to learn, and the sheet still asked for
    /// one. A slot with nothing to pick never counts.
    /// </summary>
    [Test]
    public async Task ALanguageWithNothingLeftToLearnIsNotAPickLeft()
    {
        await Server.WaitAssertion(() =>
        {
            var build = new CFCharacterBuildData
            {
                Class = "Sorcerer",
                AbilityBoostsLevel5 = new List<CFAbility> { CFAbility.Intelligence },
            };
            var modifiers = new Dictionary<CFAbility, int> { [CFAbility.Intelligence] = 0 };
            Assert.That(CFLevelUp.NeedsLanguage(build, 5, modifiers), "The boost raises Intelligence.");

            var all = CFLevelUp.LanguageOptions(Protos, build, null, modifiers)
                .Select(language => new ProtoId<CFLanguagePrototype>(language.ID))
                .ToList();
            Assert.That(all, Is.Not.Empty);

            bool AsksForLanguage(IReadOnlyCollection<ProtoId<CFLanguagePrototype>> known)
            {
                return CFLevelUp.UnfilledSlots(Protos, build, null, 5, modifiers, known)
                    .Any(slot => slot.StartsWith(nameof(CFLevelSlot.Language)));
            }

            Assert.Multiple(() =>
            {
                Assert.That(AsksForLanguage(null), "With languages left, it is a pick.");
                Assert.That(AsksForLanguage(all), Is.False, "Knowing them all already, there is nothing to pick.");
            });

            build.Languages.Add(all[0]);
            Assert.That(AsksForLanguage(null), Is.False, "Picked.");
        });
    }

    [Test]
    public async Task TakingALevelWithPicksOpenWritesAReport()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string file = null;
        try
        {
            await Server.WaitAssertion(() =>
            {
                var hero = Hero(new CFCharacterBuildData { Class = "Fighter" });
                var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
                var experience = SEntMan.System<CFExperienceSystem>();
                var reports = SEntMan.System<CFLevelUpReportSystem>();
                var started = System.DateTime.UtcNow.AddSeconds(-1);

                experience.AddXp(hero, CFExperienceComponent.XpPerLevel);
                Assert.That(experience.TryLevelUp(hero, xp.Build!.Clone()), "Open picks do not stop the level.");
                Assert.That(Level(hero), Is.EqualTo(2));

                var folder = reports.ReportFolder();
                Assert.That(System.IO.Directory.Exists(folder), folder);
                file = System.IO.Directory.GetFiles(folder, "*_level2.json")
                    .Where(path => System.IO.File.GetLastWriteTimeUtc(path) >= started)
                    .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                Assert.That(file, Is.Not.Null, "A report is written.");

                var report = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(file!))!;
                Assert.Multiple(() =>
                {
                    Assert.That(report["toLevel"]!.GetValue<int>(), Is.EqualTo(2));
                    Assert.That(report["unfilled"]!.AsArray().Select(node => node!.GetValue<string>()),
                        Has.Some.StartsWith(nameof(CFLevelSlot.ClassFeat)));
                    Assert.That(report["slots"]![nameof(CFLevelSlot.ClassFeat)]!["options"]!.AsArray(), Is.Not.Empty);
                    Assert.That(report["buildAfter"], Is.Not.Null);
                });
            });
        }
        finally
        {
            if (file != null && System.IO.File.Exists(file))
                System.IO.File.Delete(file);
        }
    }

    [Test]
    public async Task ATakenLevelWithEveryPickMadeWritesNoReport()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(new CFCharacterBuildData { Class = "Fighter" });
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            var sent = xp.Build!.Clone();
            CFLevelUp.SetPick(sent, CFLevelSlot.ClassFeat, 2, CFLevelUp.FeatOptions(Protos, sent, xp.AncestryId, CFLevelSlot.ClassFeat, 2).First().ID);
            CFLevelUp.SetPick(sent, CFLevelSlot.SkillFeat, 2, CFLevelUp.FeatOptions(Protos, sent, xp.AncestryId, CFLevelSlot.SkillFeat, 2).First().ID);

            var unfilled = CFLevelUp.UnfilledSlots(Protos, sent, xp.AncestryId, 2, new Dictionary<CFAbility, int>());
            Assert.That(unfilled, Is.Empty);
            Assert.That(SEntMan.System<CFLevelUpReportSystem>()
                .Report(hero, 2, unfilled, xp.Build, sent, sent, xp.AncestryId, new Dictionary<CFAbility, int>()), Is.Null);
        });
    }

    [Test]
    public async Task ACasterFillsTheSlotsItsLevelBrings()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(new CFCharacterBuildData { Class = "Sorcerer" });
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            var sent = xp.Build!.Clone();

            var slots = CFLevelUp.NewSpellSlots(Protos, sent, 2);
            Assert.That(slots, Is.Not.Empty, "A sorcerer gains a slot at level 2.");
            var picked = new List<string>();
            foreach (var slot in slots)
            {
                var spell = Protos.EnumeratePrototypes<CFSpellPrototype>()
                    .First(spell => spell.Rank == slot.Rank && Protos.HasIndex<EntityPrototype>("CF" + spell.ID) && !picked.Contains(spell.ID));
                picked.Add(spell.ID);
                sent.Spells.Add(new CFSpellPick { Rank = slot.Rank, Index = slot.Index, Spell = spell.ID });
            }

            var experience = SEntMan.System<CFExperienceSystem>();
            experience.AddXp(hero, CFExperienceComponent.XpPerLevel);
            Assert.That(experience.TryLevelUp(hero, sent));
            Assert.That(xp.SpellPicks.Select(pick => pick.Spell.Id), Is.SupersetOf(picked));
        });
    }

    [Test]
    public async Task TheSheetHasALevelUpTabAndGhostsCanRespawn()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new Content.Client.UserInterface.Systems.Character.Windows.CharacterWindow();
            Assert.That(window.LevelUpTab.Parent, Is.EqualTo(window.Tabs));
            _ = new Content.Client.UserInterface.Systems.Ghost.Widgets.GhostGui();
        });
    }

    [Test]
    public async Task TheLobbyOffersLevelOneSlotsOnly()
    {
        await Server.WaitAssertion(() =>
        {
            var wizard = Protos.Index(new ProtoId<CFClassPrototype>("Wizard"));
            var casting = wizard.Spellcasting!;
            var ranks = CFSpellSlots.ForClass(wizard);
            Assert.Multiple(() =>
            {
                Assert.That(ranks.Max(rank => rank.Rank), Is.EqualTo(1), "Only cantrips and rank 1 at level 1.");
                Assert.That(ranks.Single(rank => rank.Rank == 1).Count, Is.EqualTo(casting.SlotsAt(1)[0]));
            });
        });
    }
}
