using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Cantrips and skills picked for a feat, heritage or subclass, on the real options.</summary>
[TestFixture]
[TestOf(typeof(CFOptionChoices))]
public sealed class CFOptionChoiceTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    private CFDevMap _map = default!;

    private static CFOptionPick Pick(string source, int slot, string value)
    {
        return new CFOptionPick { Source = source, Slot = slot, Value = value };
    }

    private EntityUid Built(string ancestry, CFCharacterBuildData build)
    {
        var species = Server.ProtoMan.Index<CFAncestryPrototype>(ancestry).Species;
        var hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies(species).WithCFBuild(build));
        return hero;
    }

    private bool HasAction(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private List<string> Trained(EntityUid hero)
    {
        return SEntMan.GetComponent<CFCombatantComponent>(hero).TrainedSkills.ToList();
    }

    private List<string> ArcaneCantrips()
    {
        return CFOptionChoices.Allowed(Server.ProtoMan, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFOtherworldlyMagic")).Choices[0]);
    }

    [Test]
    public async Task AChosenCantripCanBeCast()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var has = false;
        var count = 0;
        await Server.WaitPost(() =>
        {
            var cantrips = ArcaneCantrips();
            count = cantrips.Count;
            var build = new CFCharacterBuildData { AncestryFeat = "CFOtherworldlyMagic" };
            build.OptionPicks.Add(Pick("CFOtherworldlyMagic", 0, cantrips[0]));
            has = HasAction(Built("Elf", build), "CF" + cantrips[0]);
        });

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(has, Is.True);
        });
    }

    [Test]
    public async Task ACantripFromTheWrongListIsRefused()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var picks = -1;
        var has = true;
        await Server.WaitPost(() =>
        {
            var arcane = ArcaneCantrips();
            var outside = Server.ProtoMan.EnumeratePrototypes<CFSpellPrototype>().First(spell => spell.Rank == 0 && !arcane.Contains(spell.ID));
            var build = new CFCharacterBuildData { AncestryFeat = "CFOtherworldlyMagic" };
            build.OptionPicks.Add(Pick("CFOtherworldlyMagic", 0, outside.ID));
            var hero = Built("Elf", build);
            picks = SEntMan.GetComponent<CFExperienceComponent>(hero).OptionPicks.Count;
            has = HasAction(hero, "CF" + outside.ID);
        });

        Assert.Multiple(() =>
        {
            Assert.That(picks, Is.Zero);
            Assert.That(has, Is.False);
        });
    }

    [Test]
    public async Task TwoSkillsMustBeDifferent()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var trained = new List<string>();
        var repeated = -1;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { AncestryFeat = "CFNaturalSkill" };
            build.OptionPicks.Add(Pick("CFNaturalSkill", 0, "Athletics"));
            build.OptionPicks.Add(Pick("CFNaturalSkill", 1, "Medicine"));
            trained = Trained(Built("Human", build));

            var same = new CFCharacterBuildData { AncestryFeat = "CFNaturalSkill" };
            same.OptionPicks.Add(Pick("CFNaturalSkill", 0, "Athletics"));
            same.OptionPicks.Add(Pick("CFNaturalSkill", 1, "Athletics"));
            repeated = CFOptionChoices.Valid(Server.ProtoMan, same, "Human").Count;
        });

        Assert.Multiple(() =>
        {
            Assert.That(trained, Does.Contain("Athletics"));
            Assert.That(trained, Does.Contain("Medicine"));
            Assert.That(repeated, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task APickWaitsForItsFeatsLevel()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var before = true;
        var after = false;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { AncestryFeatLevel5 = "CFNaturalSkill" };
            build.OptionPicks.Add(Pick("CFNaturalSkill", 0, "Athletics"));
            build.OptionPicks.Add(Pick("CFNaturalSkill", 1, "Medicine"));
            var hero = Built("Human", build);
            before = Trained(hero).Contains("Athletics");
            SEntMan.System<CFExperienceSystem>().AddXpAndLevelUp(hero, 4 * CFExperienceComponent.XpPerLevel);
            after = Trained(hero).Contains("Athletics");
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(after, Is.True);
        });
    }

    [Test]
    public async Task HeritagesAndSubclassesAskToo()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var heritage = new List<string>();
        var racket = new List<string>();
        var offList = new List<string>();
        await Server.WaitPost(() =>
        {
            var human = new CFCharacterBuildData { Heritage = "SkilledHuman" };
            human.OptionPicks.Add(Pick("SkilledHuman", 0, "Survival"));
            heritage = Trained(Built("Human", human));

            var mastermind = new CFCharacterBuildData { Class = "Rogue" };
            mastermind.ClassChoices.Add("Mastermind");
            mastermind.OptionPicks.Add(Pick("Mastermind", 0, "Occultism"));
            racket = Trained(Built("Human", mastermind));

            var wrong = new CFCharacterBuildData { Class = "Rogue" };
            wrong.ClassChoices.Add("Mastermind");
            wrong.OptionPicks.Add(Pick("Mastermind", 0, "Athletics"));
            offList = Trained(Built("Human", wrong));
        });

        Assert.Multiple(() =>
        {
            Assert.That(heritage, Does.Contain("Survival"));
            Assert.That(racket, Does.Contain("Occultism"));
            Assert.That(offList, Does.Not.Contain("Athletics"), "A Mastermind picks from a short list.");
        });
    }

    [Test]
    public async Task APickForAnOptionNoLongerTakenIsDropped()
    {
        var valid = -1;
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData();
            build.OptionPicks.Add(Pick("CFNaturalSkill", 0, "Athletics"));
            valid = CFOptionChoices.Valid(Server.ProtoMan, build, "Human").Count;
        });

        Assert.That(valid, Is.Zero);
    }

    [Test]
    public async Task LizardfolkLoreKeepsSurvivalAndAddsTheChoice()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var trained = new List<string>();
        await Server.WaitPost(() =>
        {
            var build = new CFCharacterBuildData { AncestryFeat = "CFLizardfolkLore" };
            build.OptionPicks.Add(Pick("CFLizardfolkLore", 0, "Occultism"));
            trained = Trained(Built("Lizardfolk", build));
        });

        Assert.Multiple(() =>
        {
            Assert.That(trained, Does.Contain("Survival"));
            Assert.That(trained, Does.Contain("Occultism"));
            Assert.That(trained, Does.Not.Contain("Nature"));
        });
    }
}
