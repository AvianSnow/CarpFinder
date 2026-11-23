using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Exemplar's feats and epithets, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFIkonSystem))]
public sealed class CFExemplarTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 7, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFExemplarHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 400
    classId: Exemplar
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
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
  parent: CFExemplarHero
  id: CFExemplarFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    armorClass: 1
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
      Dexterity: 0
      Constitution: 0
      Intelligence: 0
      Wisdom: -40
      Charisma: 0
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFExemplarFoe
  id: CFExemplarUnholy
  components:
  - type: CFTrait
    traits:
    - Unholy
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFIkonSystem Ikons => SEntMan.System<CFIkonSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private CFEffectSystem Effects => SEntMan.System<CFEffectSystem>();

    private EntityUid Spawn(string id = "CFExemplarHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Exemplar(params string[] ikons)
    {
        var hero = Spawn();
        foreach (var ikon in ikons)
        {
            Ikons.Add(hero, ikon);
        }

        return hero;
    }

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(id)).Grants, id);
    }

    private void Choose(EntityUid hero, string id)
    {
        var choice = Server.ProtoMan.Index<CFClassChoicePrototype>(id);
        Features.Apply(hero, choice.Grants, id);
        foreach (var feature in choice.Features)
        {
            Features.Apply(hero, feature.Grants, id);
        }
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

    private int Hp(EntityUid mob) => SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    /// <summary>Energized Spark changes what the ikons deal, Sanctified Soul gives a side.</summary>
    [Test]
    public async Task EnergizedSparkAndSanctifiedSoulTakeTheirPicks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var spark = "";
        var unholy = false;
        await Server.WaitPost(() =>
        {
            var hero = Exemplar("GleamingBlade");
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFEnergizedSpark", Slot = 0, Value = "Fire" });
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFSanctifiedSoul", Slot = 0, Value = "pick:SoulUnholy" });
            GiveFeat(hero, "CFEnergizedSpark");
            GiveFeat(hero, "CFSanctifiedSoul");
            spark = SEntMan.GetComponent<CFExemplarComponent>(hero).SparkType;
            unholy = SEntMan.GetComponent<CFTraitComponent>(hero).HasTrait("Unholy");
        });

        Assert.Multiple(() =>
        {
            Assert.That(spark, Is.EqualTo("Fire"));
            Assert.That(unholy, Is.True);
        });
    }

    /// <summary>Vow of Mortal Defiance marks a holy or unholy enemy, and nobody else.</summary>
    [Test]
    public async Task VowOfMortalDefianceMarksTheUnholy()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid? marked = null;
        EntityUid? plainMarked = null;
        EntityUid unholy = default;
        var die = 0;
        await Server.WaitPost(() =>
        {
            var hero = Exemplar("GleamingBlade");
            GiveFeat(hero, "CFVowofMortalDefiance");
            var action = Action(hero, "CFActVowOfMortalDefiance")!.Value;
            var plain = Spawn("CFExemplarFoe", 2);
            Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = plain });
            plainMarked = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).MarkedTarget;

            unholy = Spawn("CFExemplarUnholy", 3);
            Actions.ClearCooldown(action);
            Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = unholy });
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            marked = options.MarkedTarget;
            die = options.MarkedDamageDie;
        });

        Assert.Multiple(() =>
        {
            Assert.That(plainMarked, Is.Null, "Only a holy or unholy creature.");
            Assert.That(marked, Is.EqualTo(unholy));
            Assert.That(die, Is.EqualTo(6));
        });
    }

    /// <summary>Red-Gold Mortality: a wound from the empowered ikon leaves the creature healing less. Wisdom -40 always fails.</summary>
    [Test]
    public async Task RedGoldMortalityHaltsHealing()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            var hero = Exemplar("GleamingBlade");
            GiveFeat(hero, "CFRedGoldMortality");
            var foe = Spawn("CFExemplarFoe", 1);
            var strikes = SEntMan.System<CFStrikeSystem>();
            strikes.PerformStrike(hero, foe, strikes.UnarmedStats(hero), hero, melee: true);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(foe);
            combat.CurrentHp = 100;
            before = combat.CurrentHp;
            Effects.Heal(hero, foe, 50);
            after = combat.CurrentHp;
        });

        // A critical failure blocks healing; a failure halves it. Either way, far less than the 50 asked.
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(100));
            Assert.That(after, Is.LessThanOrEqualTo(125));
            Assert.That(after, Is.AnyOf(100, 125));
        });
    }

    /// <summary>Leap the Falls adds 5 feet with the spark in a body ikon, not in a weapon.</summary>
    [Test]
    public async Task LeapTheFallsNeedsABodyIkon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int body = 0, weapon = 0, without = 0;
        await Server.WaitPost(() =>
        {
            var hero = Exemplar("ScarOfTheSurvivor", "GleamingBlade");
            without = Ikons.LeapBonusFeet(hero);
            GiveFeat(hero, "CFLeaptheFalls");
            body = Ikons.LeapBonusFeet(hero);
            Ikons.SetSpark(hero, 1);
            weapon = Ikons.LeapBonusFeet(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.EqualTo(0));
            Assert.That(body, Is.EqualTo(5));
            Assert.That(weapon, Is.EqualTo(0));
        });
    }

    /// <summary>A feat's transcendence is enabled for the kind of ikon it fits: Steel on Steel with a melee weapon, not a body.</summary>
    [Test]
    public async Task FeatTranscendencesFollowTheKindOfIkon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var withSword = false;
        var withScar = false;
        var threads = false;
        await Server.WaitPost(() =>
        {
            var hero = Exemplar("GleamingBlade", "ScarOfTheSurvivor");
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, Spawn("CFLongsword"), animate: false);
            GiveFeat(hero, "CFSteelonSteel");
            GiveFeat(hero, "CFFollowtheThreadsofFate");
            Ikons.SetSpark(hero, 0);
        });
        await Server.WaitRunTicks(20);
        await Server.WaitPost(() =>
        {
            var hero = SEntMan.EntityQuery<CFExemplarComponent>().Select(c => c.Owner).Last();
            withSword = Action(hero, "CFActTransRingingChallenge")!.Value.Comp.Enabled;
            Ikons.SetSpark(hero, 1);
        });
        await Server.WaitRunTicks(20);
        await Server.WaitPost(() =>
        {
            var hero = SEntMan.EntityQuery<CFExemplarComponent>().Select(c => c.Owner).Last();
            withScar = Action(hero, "CFActTransRingingChallenge")!.Value.Comp.Enabled;
            threads = Action(hero, "CFActTransUnravelTheFuture")!.Value.Comp.Enabled;
        });

        Assert.Multiple(() =>
        {
            Assert.That(withSword, Is.True);
            Assert.That(withScar, Is.False);
            Assert.That(threads, Is.True, "Any ikon.");
        });
    }

    /// <summary>Ringing Challenge hurts the creatures around the Exemplar and spends the spark.</summary>
    [Test]
    public async Task RingingChallengeHurtsAround()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hurt = 0;
        var far = 0;
        var spark = -1;
        await Server.WaitPost(() =>
        {
            var hero = Exemplar("GleamingBlade", "ScarOfTheSurvivor");
            GiveFeat(hero, "CFSteelonSteel");
            var near = new EntityUid[4];
            for (var i = 0; i < near.Length; i++)
            {
                near[i] = Spawn("CFExemplarFoe", 2);
            }

            var distant = Spawn("CFExemplarFoe", 6);
            var ev = new CFActivityInstantEvent { Performer = hero };
            SEntMan.EventBus.RaiseLocalEvent(Action(hero, "CFActTransRingingChallenge")!.Value.Owner, ev);
            hurt = near.Sum(foe => 500 - Hp(foe));
            far = 500 - Hp(distant);
            spark = SEntMan.GetComponent<CFExemplarComponent>(hero).Spark;
        });

        Assert.Multiple(() =>
        {
            Assert.That(hurt, Is.GreaterThan(0));
            Assert.That(far, Is.EqualTo(0));
            Assert.That(spark, Is.EqualTo(1), "The spark moves on.");
        });
    }

    /// <summary>The Proud epithet Taunts an enemy after a transcendence, and the Mournful dazzles one.</summary>
    [Test]
    public async Task EpithetsFollowATranscendence()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var taunted = false;
        var dazzled = 0;
        await Server.WaitPost(() =>
        {
            var proud = Exemplar("GleamingBlade", "ScarOfTheSurvivor");
            GiveFeat(proud, "CFFollowtheThreadsofFate");
            Choose(proud, "EpithetProud");
            var foe = Spawn("CFExemplarFoe", 2);
            SEntMan.EventBus.RaiseLocalEvent(Action(proud, "CFActTransUnravelTheFuture")!.Value.Owner, new CFActivityInstantEvent { Performer = proud });
            taunted = SEntMan.System<CFTauntSystem>().IsTauntedBy(foe, proud);

            var mournful = Spawn(x: 4);
            Ikons.Add(mournful, "GleamingBlade");
            Ikons.Add(mournful, "ScarOfTheSurvivor");
            GiveFeat(mournful, "CFFollowtheThreadsofFate");
            Choose(mournful, "EpithetMournful");
            var other = Spawn("CFExemplarFoe", 5);
            SEntMan.EventBus.RaiseLocalEvent(Action(mournful, "CFActTransUnravelTheFuture")!.Value.Owner, new CFActivityInstantEvent { Performer = mournful });
            dazzled = SEntMan.System<CFConditionSystem>().Value(other, CFConditionKind.Dazzled);
        });

        Assert.Multiple(() =>
        {
            Assert.That(taunted, Is.True);
            Assert.That(dazzled, Is.EqualTo(1));
        });
    }

    /// <summary>Lightning Swap stows what is held and draws the worn weapons.</summary>
    [Test]
    public async Task LightningSwapDrawsFromTheBelt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var held = new System.Collections.Generic.List<string>();
        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var inventory = SEntMan.System<InventorySystem>();
            var hero = Exemplar("GleamingBlade");
            GiveFeat(hero, "CFLightningSwapExemplar");
            hands.TryPickupAnyHand(hero, Spawn("CFLongsword"), animate: false);
            inventory.TryEquip(hero, Spawn("CFDagger"), "belt", silent: true, force: true);
            inventory.TryEquip(hero, Spawn("CFSpear"), "back", silent: true, force: true);
            SEntMan.EventBus.RaiseLocalEvent(Action(hero, "CFActLightningSwap")!.Value.Owner, new CFActivityInstantEvent { Performer = hero });
            foreach (var item in hands.EnumerateHeld(hero))
            {
                held.Add(SEntMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID ?? "");
            }
        });

        Assert.That(held, Is.EquivalentTo(new[] { "CFDagger", "CFSpear" }));
    }

    /// <summary>Every Exemplar feat applies.</summary>
    [Test]
    public async Task EveryExemplarFeatCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitAssertion(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class == "Exemplar"))
            {
                Features.Apply(Spawn(), feat.Grants, feat.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(13));
    }
}
