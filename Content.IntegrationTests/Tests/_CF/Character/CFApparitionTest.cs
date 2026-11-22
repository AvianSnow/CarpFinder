using Robust.Shared.Prototypes;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>An Animist's apparitions, apparition slots and sustained vessel spells, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFApparitionSystem))]
public sealed class CFApparitionTest : GameTest
{
    private static readonly Box2i Footprint = new(-2, -2, 4, 3);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFApparitionHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 400
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFApparitionHero
  id: CFApparitionFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private TimeSpan Now => Server.ResolveDependency<IGameTiming>().CurTime;

    private EntityUid Spawn(string id, int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private void Choose(EntityUid hero, params string[] choices)
    {
        foreach (var id in choices)
        {
            Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);
        }
    }

    private EntityUid Animist(params string[] choices)
    {
        var hero = Spawn("CFApparitionHero");
        var first = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Animist")).Features.First(f => f.Name == "Animist & Apparition Spellcasting");
        Features.Apply(hero, first.Grants, "casting");
        Choose(hero, choices);
        return hero;
    }

    private EntityUid? Action(EntityUid hero, string id)
    {
        var actions = SEntMan.System<Content.Shared.Actions.SharedActionsSystem>();
        foreach (var action in actions.GetActions(hero))
        {
            if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id)
                return action;
        }

        return null;
    }

    private void Cast(EntityUid hero, EntityUid action)
    {
        var ev = new CFSpellCastInstantEvent { Performer = hero };
        SEntMan.EventBus.RaiseLocalEvent(action, ev);
    }

    private CFSpellbookComponent Book(EntityUid hero) => SEntMan.GetComponent<CFSpellbookComponent>(hero);

    [Test]
    public async Task EveryApparitionCanBeTakenInEitherSlot()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Group.Length > 0 && c.Class.Id == "Animist"))
            {
                Choose(Spawn("CFApparitionHero"), choice.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(28));
    }

    [Test]
    public async Task ABuildCannotAttuneToTheSameApparitionTwice()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var build = new CFCharacterBuildData { Class = "Animist" };
            CFClassChoices.Assign(Server.ProtoMan, build, "PrimaryWitness");
            CFClassChoices.Assign(Server.ProtoMan, build, "SecondWitness");
            Assert.That(build.ClassChoices.Select(c => c.Id), Is.EquivalentTo(new[] { "SecondWitness" }));

            // A client could send both, so the list that decides what is granted has to refuse it as well.
            build.ClassChoices.Add("PrimaryWitness");
            Assert.That(CFClassChoices.Selected(Server.ProtoMan, build), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ApparitionSpellsSpendTheirOwnSlots()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Animist("PrimaryWitness");
            var book = Book(hero);
            book.Prepared = true;
            var system = SEntMan.System<CFSpellSystem>();
            var sure = Action(hero, "CFSureStrike");
            Assert.That(sure, Is.Not.Null);
            var spell = SEntMan.GetComponent<CFSpellComponent>(sure!.Value);

            Assert.That(system.HasSlot(hero, spell), Is.True);
            Assert.That(CFApparitions.UsesSlot(book, spell.Spell, spell.Rank), Is.True);

            CFApparitions.Spend(book, spell.Rank);
            Assert.That(system.HasSlot(hero, spell), Is.False);

            // A prepared charge of the same spell is still there to be used.
            book.PreparedSlots.Add(new CFPreparedSlot(spell.Spell, spell.Rank));
            Assert.That(CFApparitions.UsesSlot(book, spell.Spell, spell.Rank), Is.False);
        });
    }

    /// <summary>A sustained spell keeps itself going: no second cast, no second Focus Point, and only a dispel ends it early.</summary>
    [Test]
    public async Task ASustainedSpellRunsItselfUntilItIsDispelled()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        EntityUid action = default;
        bool running = false, again = true;
        var spent = -1;
        await Server.WaitPost(() =>
        {
            hero = Animist("PrimaryWitness");
            action = Action(hero, "CFEmbodimentOfBattle")!.Value;
            Cast(hero, action);
            var spells = SEntMan.System<CFSpellSystem>();
            running = spells.IsSustaining(hero, "EmbodimentOfBattle");

            // Using it again while it runs does nothing and costs nothing.
            Cast(hero, action);
            spent = Book(hero).FocusSpent;
            again = spells.HasSlot(hero, SEntMan.GetComponent<CFSpellComponent>(action));
        });

        // Its bonus lasts 12 seconds a time, so 20 seconds on it is only still there if the spell ran again by itself.
        await Pair.RunSeconds(20f);
        int kept = 0;
        bool dispelled = false, stillRunning = true;
        await Server.WaitPost(() =>
        {
            kept = SEntMan.System<CFBonusSystem>().Total(hero, CFBonusStat.AttackRoll);
            dispelled = SEntMan.System<CFCounteractSystem>().TryDispel(Spawn("CFApparitionFoe", 1), hero, 3, 50, d20: 20);
            stillRunning = SEntMan.System<CFSpellSystem>().IsSustaining(hero, "EmbodimentOfBattle");
        });

        await Pair.RunSeconds(20f);
        var after = -1;
        await Server.WaitPost(() => after = SEntMan.System<CFBonusSystem>().Total(hero, CFBonusStat.AttackRoll));

        Assert.Multiple(() =>
        {
            Assert.That(running, Is.True);
            Assert.That(spent, Is.EqualTo(1), "One Focus Point for the whole minute.");
            Assert.That(again, Is.False, "It cannot be cast again while it runs.");
            Assert.That(kept, Is.EqualTo(1), "It kept itself going.");
            Assert.That(dispelled, Is.True);
            Assert.That(stillRunning, Is.False, "Dispel Magic ends it.");
            Assert.That(after, Is.Zero, "And it does not come back.");
        });
    }

    [Test]
    public async Task CircleOfSpiritsSwapsTheVesselSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Animist("SecondSteward", "PrimaryWitness");
            Assert.That(Action(hero, "CFEmbodimentOfBattle"), Is.Not.Null);
            Assert.That(Action(hero, "CFEarthsBile"), Is.Null);

            Assert.That(SEntMan.System<CFApparitionSystem>().ChangePrimary(hero), Is.True);
            Assert.That(Action(hero, "CFEmbodimentOfBattle"), Is.Null);
            Assert.That(Action(hero, "CFEarthsBile"), Is.Not.Null);
        });
    }

    [Test]
    public async Task AVesselFormGivesItsAttackBack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = Animist("PrimaryLurker");
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            options.NaturalDice = 0;

            var form = new CFFormAttackEffect { Dice = 1, Die = 8, DamageType = "bludgeoning", DurationSeconds = 0f };
            SEntMan.System<CFApparitionSystem>().StartForm(hero, form);
            Assert.That(options.NaturalDie, Is.EqualTo(8));
            Assert.That(options.NaturalDice, Is.EqualTo(1));
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CFCombatOptionsComponent>(hero).NaturalDice, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task StoreTimeGivesTheReactionBack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Animist("PrimaryEcho");
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            options.ReactionSpent = 1;

            var action = Action(hero, "CFStoreTime");
            Assert.That(action, Is.Not.Null);
            SEntMan.System<CFEffectSystem>().ResolveTarget(action!.Value, hero, hero);
            Assert.That(options.ReactionSpent, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task GardenOfHealingHealsAroundTheCaster()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Animist("PrimaryCustodian");
            var action = Action(hero, "CFGardenOfHealing");
            Assert.That(action, Is.Not.Null);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
            var before = combat.CurrentHp;
            var at = new MapCoordinates(SEntMan.System<SharedTransformSystem>().GetWorldPosition(hero), SEntMan.GetComponent<TransformComponent>(hero).MapID);
            SEntMan.System<CFEffectSystem>().ResolveArea(action!.Value, hero, at);
            Assert.That(combat.CurrentHp, Is.GreaterThan(before));
        });
    }
}
