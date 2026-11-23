using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Animist's practices, vessel spells and feats, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFAnimistSystem))]
public sealed class CFAnimistTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 7, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFAnimistHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 400
    classId: Animist
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
      Dexterity: 0
      Constitution: 0
      Intelligence: 0
      Wisdom: 0
      Charisma: 0
  - type: CFCombatOptions
  - type: CFExperience
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFAnimistHero
  id: CFAnimistFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFAnimistFoe
  id: CFAnimistUndead
  components:
  - type: CFTrait
    traits:
    - Undead
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private CFEffectSystem Effects => SEntMan.System<CFEffectSystem>();

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private EntityUid Spawn(string id = "CFAnimistHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(id)).Grants, id);
    }

    private void Choose(EntityUid hero, params string[] choices)
    {
        foreach (var id in choices)
        {
            Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);
        }
    }

    private EntityUid Animist(params string[] choices)
    {
        var hero = Spawn();
        var first = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Animist")).Features.First(f => f.Name == "Animist & Apparition Spellcasting");
        Features.Apply(hero, first.Grants, "casting");
        Choose(hero, choices);
        return hero;
    }

    private Entity<ActionComponent>? Action(EntityUid hero, string id)
    {
        foreach (var action in Actions.GetActions(hero))
        {
            if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id)
                return action;
        }

        return null;
    }

    private void EnterStance(EntityUid hero, string id)
    {
        Actions.PerformAction(hero, Action(hero, id)!.Value, new CFActivityInstantEvent());
    }

    /// <summary>Channeler's Stance adds the spell's rank to an apparition spell's damage and healing, and to nothing else.</summary>
    [Test]
    public async Task ChannelersStanceAddsTheRankToApparitionSpells()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = 0, inStance = 0, other = 0, vessel = 0;
        await Server.WaitPost(() =>
        {
            var hero = Animist("PrimaryWitness", "SecondCustodian");
            GiveFeat(hero, "CFChannelersStance");
            GiveFeat(hero, "CFEmbodimentoftheBalance");
            var foe = Spawn("CFAnimistFoe", 1);
            var harm = SEntMan.SpawnEntity("CFHarm", MapCoordinates.Nullspace);
            var fireball = SEntMan.SpawnEntity("CFFireball", MapCoordinates.Nullspace);
            var garden = SEntMan.SpawnEntity("CFGardenOfHealing", MapCoordinates.Nullspace);
            before = Effects.PotencyBonus(Effects.BuildArgs(harm, hero, foe, null, null, out _));

            EnterStance(hero, "CFActChannelersStance");
            inStance = Effects.PotencyBonus(Effects.BuildArgs(harm, hero, foe, null, null, out _));
            other = Effects.PotencyBonus(Effects.BuildArgs(fireball, hero, foe, null, null, out _));
            vessel = Effects.PotencyBonus(Effects.BuildArgs(garden, hero, hero, null, null, out _));
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(0));
            Assert.That(inStance, Is.EqualTo(1), "Harm is an apparition spell with Embodiment of the Balance.");
            Assert.That(other, Is.EqualTo(0), "Fireball is not one.");
            Assert.That(vessel, Is.EqualTo(1), "A vessel spell counts, at the rank focus spells have.");
        });
    }

    /// <summary>Channeled Protection: a spell cast from a slot in the stance shields the caster and the allies beside it.</summary>
    [Test]
    public async Task ChanneledProtectionShieldsNeighbours()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int self = 0, near = 0, far = 0, noStance = 0;
        await Server.WaitPost(() =>
        {
            var animist = SEntMan.System<CFAnimistSystem>();
            var hero = Animist("PrimaryWitness", "SecondCustodian");
            GiveFeat(hero, "CFChannelersStance");
            GiveFeat(hero, "CFChanneledProtection");
            var friend = Spawn(x: 1);
            var distant = Spawn(x: 5);
            var spell = SEntMan.GetComponent<CFSpellComponent>(SEntMan.SpawnEntity("CFHarm", MapCoordinates.Nullspace));

            animist.NoteCast(hero, spell);
            noStance = Bonuses.Total(hero, CFBonusStat.ArmorClass);

            EnterStance(hero, "CFActChannelersStance");
            animist.NoteCast(hero, spell);
            self = Bonuses.Total(hero, CFBonusStat.Reflex);
            near = Bonuses.Total(friend, CFBonusStat.ArmorClass);
            far = Bonuses.Total(distant, CFBonusStat.ArmorClass);
        });

        Assert.Multiple(() =>
        {
            Assert.That(noStance, Is.EqualTo(0));
            Assert.That(self, Is.EqualTo(1));
            Assert.That(near, Is.EqualTo(1));
            Assert.That(far, Is.EqualTo(0));
        });
    }

    /// <summary>Relinquish Control: +4 status to saves against mental effects, and the Medium has it.</summary>
    [Test]
    public async Task RelinquishControlGuardsTheMind()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int mental = 0, fire = 0;
        await Server.WaitPost(() =>
        {
            var hero = Animist("PracticeMedium");
            Actions.PerformAction(hero, Action(hero, "CFActRelinquishControl")!.Value, new CFActivityInstantEvent());
            mental = Bonuses.Total(hero, CFBonusStat.Saves, traits: new() { "Mental" });
            fire = Bonuses.Total(hero, CFBonusStat.Saves, traits: new() { "Fire" });
        });

        Assert.Multiple(() =>
        {
            Assert.That(mental, Is.EqualTo(4));
            Assert.That(fire, Is.EqualTo(0));
        });
    }

    /// <summary>Apparition Sense works on undead and spirits only. The Seer has it, and +1 AC against undead.</summary>
    [Test]
    public async Task ApparitionSenseFindsOnlyTheDead()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var undead = false;
        var living = false;
        int ac = 0, acLiving = 0;
        await Server.WaitPost(() =>
        {
            var hero = Animist("PracticeSeer");
            var sense = SEntMan.GetComponent<CFSeekSenseComponent>(hero);
            undead = CFSeekSenses.Covers(SEntMan, sense, Spawn("CFAnimistUndead", 1));
            living = CFSeekSenses.Covers(SEntMan, sense, Spawn("CFAnimistFoe", 2));
            ac = Bonuses.Total(hero, CFBonusStat.ArmorClass, traits: new() { "Undead" });
            acLiving = Bonuses.Total(hero, CFBonusStat.ArmorClass);
        });

        Assert.Multiple(() =>
        {
            Assert.That(undead, Is.True);
            Assert.That(living, Is.False);
            Assert.That(ac, Is.EqualTo(1));
            Assert.That(acLiving, Is.EqualTo(0));
        });
    }

    /// <summary>Traveling Workshop trains the Crafter in Crafting and Thievery for a minute, and no longer.</summary>
    [Test]
    public async Task TravelingWorkshopLendsCraftingAndThievery()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        CFProficiencyRank before = CFProficiencyRank.Trained, during = CFProficiencyRank.Untrained, thievery = CFProficiencyRank.Untrained, after = CFProficiencyRank.Trained;
        await Server.WaitPost(() =>
        {
            hero = Animist("PrimaryCrafter", "SecondCustodian");
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            before = CFDefense.SkillRank(Server.ProtoMan, combat, "Crafting");
            Actions.PerformAction(hero, Action(hero, "CFTravelingWorkshop")!.Value, new CFSpellCastInstantEvent());
            during = CFDefense.SkillRank(Server.ProtoMan, combat, "Crafting");
            thievery = CFDefense.SkillRank(Server.ProtoMan, combat, "Thievery");
            foreach (var lent in combat.TemporarySkills)
            {
                lent.Expires = Server.ResolveDependency<IGameTiming>().CurTime;
            }
        });
        await Pair.RunTicksSync(3);
        await Server.WaitPost(() => after = CFDefense.SkillRank(Server.ProtoMan, SEntMan.GetComponent<CFCombatantComponent>(hero), "Crafting"));

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(CFProficiencyRank.Untrained));
            Assert.That(during, Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(thievery, Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(after, Is.EqualTo(CFProficiencyRank.Untrained));
        });
    }

    /// <summary>The Stalker's vessel is Darkened Forest Form, and Walk the Wilds adds Animal Form to the apparition spells.</summary>
    [Test]
    public async Task StalkerHasItsVesselAndWalkTheWildsAddsAnimalForm()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var vessel = false;
        var animalForm = false;
        var familiar = false;
        await Server.WaitPost(() =>
        {
            var hero = Animist("PrimaryStalker", "SecondCustodian", "PracticeShaman");
            vessel = Action(hero, "CFDarkenedForestForm") != null;
            GiveFeat(hero, "CFWalktheWilds");
            animalForm = SEntMan.GetComponent<CFSpellbookComponent>(hero).ApparitionSpells.Contains("AnimalForm");
            familiar = Action(hero, "CFAnimalForm") != null;
        });

        Assert.Multiple(() =>
        {
            Assert.That(vessel, Is.True);
            Assert.That(animalForm, Is.True);
            Assert.That(familiar, Is.True, "The spell is granted to cast from an apparition slot.");
        });
    }

    /// <summary>Every Animist feat applies.</summary>
    [Test]
    public async Task EveryAnimistFeatCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitAssertion(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class == "Animist"))
            {
                Features.Apply(Spawn(), feat.Grants, feat.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(13));
    }
}
