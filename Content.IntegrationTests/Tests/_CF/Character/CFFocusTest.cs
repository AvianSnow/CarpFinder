using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Focus spells and the first subclasses that use them, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFFocusSystem))]
public sealed class CFFocusTest : GameTest
{
    private const int StartHp = 500;

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFFocusHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFFocusHero
  id: CFFocusFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private EntityUid Spawn(string id, int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private CFSpellbookComponent Book(EntityUid hero)
    {
        return SEntMan.GetComponent<CFSpellbookComponent>(hero);
    }

    private void Cast(EntityUid hero, EntityUid target)
    {
        SEntMan.System<SharedActionsSystem>().PerformAction(hero, Action(hero, "CFForceBolt"), new CFSpellCastTargetEvent { Target = target });
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

    private static CFCharacterBuildData Build(string cls, string choice)
    {
        var build = new CFCharacterBuildData { Class = cls };
        build.ClassChoices.Add(choice);
        return build;
    }

    [Test]
    public async Task AFocusSpellCostsAPointAndStopsAtNone()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int first = 0, second = -1, spent = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFFocusHero");
            var foe = Spawn("CFFocusFoe", 1);
            Features.GrantFocusSpell(hero, "ForceBolt");
            Cast(hero, foe);
            first = Hurt(foe);
            spent = Book(hero).FocusSpent;
            Cast(hero, foe);
            second = Hurt(foe) - first;
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.InRange(2, 5), "1d4+1 and it always hits.");
            Assert.That(spent, Is.EqualTo(1));
            Assert.That(second, Is.Zero, "No Focus Points are left.");
        });
    }

    [Test]
    public async Task ThePoolStopsAtThree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var points = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFFocusHero");
            for (var i = 0; i < 5; i++)
            {
                Features.GrantFocusSpell(hero, "ForceBolt");
            }

            points = Book(hero).FocusPoints;
        });

        Assert.That(points, Is.EqualTo(3));
    }

    [Test]
    public async Task RefocusGivesAPointBackAndNeedsOneSpent()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        var full = true;
        await Server.WaitPost(() =>
        {
            hero = Spawn("CFFocusHero");
            Features.GrantFocusSpell(hero, "ForceBolt");
            var refocus = Action(hero, "CFActRefocus");
            var attempt = new Content.Shared.Actions.Events.ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(refocus, ref attempt);
            full = attempt.Cancelled;

            Book(hero).FocusSpent = 1;
            SEntMan.System<SharedActionsSystem>().PerformAction(hero, refocus);
        });

        await Pair.RunSeconds(60f);
        var halfway = 0;
        await Server.WaitPost(() => halfway = Book(hero).FocusSpent);

        await Pair.RunSeconds(95f);
        var done = 1;
        await Server.WaitPost(() => done = Book(hero).FocusSpent);

        Assert.Multiple(() =>
        {
            Assert.That(full, Is.True, "There is nothing to refocus.");
            Assert.That(halfway, Is.EqualTo(1), "It takes 10 minutes on the world clock.");
            Assert.That(done, Is.Zero);
        });
    }

    [Test]
    public async Task ALongRestRefillsFocus()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var spent = 1;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFFocusHero");
            Features.GrantFocusSpell(hero, "ForceBolt");
            Book(hero).FocusSpent = 1;
            SEntMan.System<CFLongRestSystem>().LongRest(hero);
            spent = Book(hero).FocusSpent;
        });

        Assert.That(spent, Is.Zero);
    }

    [Test]
    public async Task StormOrderGivesItsSkillAndFocusSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var points = 0;
        var skill = false;
        await Server.WaitPost(() =>
        {
            var hero = Built(Build("Druid", "OrderStorm"));
            points = Book(hero).FocusPoints;
            Action(hero, "CFTempestSurge");
            skill = SEntMan.GetComponent<CFCombatantComponent>(hero).TrainedSkills.Contains("Acrobatics");
        });

        Assert.Multiple(() =>
        {
            Assert.That(points, Is.EqualTo(1));
            Assert.That(skill, Is.True);
        });
    }

    [Test]
    public async Task WarpriestGetsArmorAtOnceAndMartialWeaponsAtThree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFProficiencyRank armor = default, fort = default, martialEarly = default, martialLate = default;
        await Server.WaitPost(() =>
        {
            var hero = Built(Build("Cleric", "DoctrineWarpriest"));
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            armor = CFDefense.ArmorRank(Server.ProtoMan, combat, "MediumArmor");
            fort = CFDefense.SaveRank(Server.ProtoMan, combat, "Fortitude");
            martialEarly = CFDefense.AttackRank(Server.ProtoMan, combat, "MartialWeapons");
            SEntMan.System<CFExperienceSystem>().AddXpAndLevelUp(hero, 2 * CFExperienceComponent.XpPerLevel);
            martialLate = CFDefense.AttackRank(Server.ProtoMan, combat, "MartialWeapons");
        });

        Assert.Multiple(() =>
        {
            Assert.That(armor, Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(fort, Is.EqualTo(CFProficiencyRank.Expert));
            Assert.That(martialEarly, Is.EqualTo(CFProficiencyRank.Untrained));
            Assert.That(martialLate, Is.EqualTo(CFProficiencyRank.Trained));
        });
    }

    [Test]
    public async Task AMuseTeachesItsSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() => Action(Built(Build("Bard", "MuseMaestro")), "CFSoothe"));
    }

    [TestCase("Cleric")]
    [TestCase("Wizard")]
    [TestCase("Bard")]
    [TestCase("Druid")]
    [TestCase("Witch")]
    public async Task NoPlaceholdersAreLeftInTheFeatures(string cls)
    {
        var left = 0;
        await Server.WaitPost(() =>
            left = Server.ProtoMan.Index<CFClassPrototype>(cls).Features.Count(feature => feature.Description.Contains("implemented later")));

        Assert.That(left, Is.Zero);
    }
}
