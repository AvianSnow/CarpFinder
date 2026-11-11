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

    [Test]
    public async Task SustainingCostsNoFocusAndCanLapse()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Animist("PrimaryWitness");
            var book = Book(hero);
            Assert.That(book.FocusPoints, Is.EqualTo(1));
            var action = Action(hero, "CFEmbodimentOfBattle");
            Assert.That(action, Is.Not.Null);

            Cast(hero, action!.Value);
            Assert.That(book.FocusSpent, Is.EqualTo(1));
            var spell = book.Sustaining[0].Spell;

            // The same round: refused, and nothing is paid.
            Assert.That(CFSustain.State(book, spell, Now), Is.EqualTo(CFSustainState.TooSoon));

            book.Sustaining[0] = new CFSustainedSpell(spell, Now, Now - TimeSpan.FromSeconds(7));
            Cast(hero, action.Value);
            Assert.That(book.FocusSpent, Is.EqualTo(1));
            Assert.That(CFSustain.State(book, spell, Now), Is.EqualTo(CFSustainState.TooSoon));

            // Left too long, it has ended, and there is no Focus Point to start it again.
            book.Sustaining[0] = new CFSustainedSpell(spell, Now, Now - TimeSpan.FromSeconds(13));
            Assert.That(CFSustain.State(book, spell, Now), Is.EqualTo(CFSustainState.None));
            Assert.That(SEntMan.System<CFSpellSystem>().HasSlot(hero, SEntMan.GetComponent<CFSpellComponent>(action.Value)), Is.False);

            // A minute is the longest it can go.
            book.Sustaining[0] = new CFSustainedSpell(spell, Now - TimeSpan.FromSeconds(61), Now - TimeSpan.FromSeconds(7));
            Assert.That(CFSustain.State(book, spell, Now), Is.EqualTo(CFSustainState.None));
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
