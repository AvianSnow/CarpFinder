using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Spell slots follow each class's table as a caster levels, and the spells picked for them arrive with them.</summary>
[TestFixture]
[TestOf(typeof(CFCharacterSpawnSystem))]
public sealed class CFSpellSlotTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSpellSlotHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 10
    currentHp: 10
  - type: CFAbilityScore
  - type: CFExperience
";

    private CFDevMap _map = default!;

    private static CFSpellPick Pick(int rank, int index, string spell)
    {
        return new CFSpellPick { Rank = rank, Index = index, Spell = spell };
    }

    private static List<CFSpellPick> Picks()
    {
        return new List<CFSpellPick>
        {
            Pick(1, 0, "Sleep"),
            Pick(1, 1, "Grease"),
            Pick(1, 2, "ForceBarrage"),
            Pick(2, 0, "Blur"),
            Pick(2, 1, "Invisibility"),
            Pick(3, 0, "Haste"),
            Pick(3, 1, "Fireball"),
        };
    }

    private EntityUid Caster(string cls, List<CFSpellPick>? picks = null)
    {
        var hero = SEntMan.SpawnEntity("CFSpellSlotHero", _map.Local(0, 0));
        var build = new CFCharacterBuildData { Class = cls, Spells = picks ?? Picks() };
        SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));
        return hero;
    }

    private void LevelTo(EntityUid hero, int level)
    {
        var combat = SEntMan.GetComponent<CFCombatantComponent>(hero);
        SEntMan.System<CFExperienceSystem>().AddXp(hero, (level - combat.Level) * CFExperienceComponent.XpPerLevel);
        Assert.That(combat.Level, Is.EqualTo(level));
    }

    private bool Knows(EntityUid hero, string action)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .Any(a => SEntMan.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID == action);
    }

    private CFSpellbookComponent Book(EntityUid hero) => SEntMan.GetComponent<CFSpellbookComponent>(hero);

    [TestCase("Wizard", new[] { 3, 3, 2 })]
    [TestCase("Cleric", new[] { 3, 3, 2 })]
    [TestCase("Druid", new[] { 3, 3, 2 })]
    [TestCase("Witch", new[] { 3, 3, 2 })]
    [TestCase("Bard", new[] { 3, 3, 2 })]
    [TestCase("Sorcerer", new[] { 4, 4, 3 })]
    [TestCase("Oracle", new[] { 4, 4, 3 })]
    [TestCase("Animist", new[] { 2, 2, 1 })]
    [TestCase("Magus", new[] { 2, 2, 1 })]
    [TestCase("Necromancer", new[] { 2, 2, 1 })]
    [TestCase("Psychic", new[] { 2, 2, 1 })]
    [TestCase("Summoner", new[] { 2, 2, 1 })]
    public async Task EachCasterReachesItsTableAtLevelFive(string cls, int[] expected)
    {
        await Server.WaitAssertion(() =>
        {
            var casting = Server.ProtoMan.Index<CFClassPrototype>(cls).Spellcasting;
            Assert.That(casting, Is.Not.Null);
            Assert.That(casting!.SlotsAt(5), Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task APreparedCasterGainsSlotsAndTheirSpells()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Caster("Wizard");
            var book = Book(hero);
            Assert.That(book.Slots, Is.EqualTo(new[] { 5, 2 }));
            Assert.That(book.PreparedSlots, Has.Count.EqualTo(2));
            Assert.That(Knows(hero, "CFForceBarrage"), Is.False, "the third rank 1 pick waits for level 2");
            Assert.That(Knows(hero, "CFBlur"), Is.False, "rank 2 waits for level 3");

            LevelTo(hero, 3);
            Assert.That(book.Slots, Is.EqualTo(new[] { 5, 3, 2 }));
            Assert.That(book.PreparedSlots, Has.Count.EqualTo(5));
            Assert.That(book.PlannedSlots, Has.Count.EqualTo(5));
            Assert.That(book.PreparableSlots, Is.EqualTo(new[] { 5, 3, 2 }));
            Assert.That(Knows(hero, "CFForceBarrage"), Is.True);
            Assert.That(Knows(hero, "CFBlur"), Is.True);
            Assert.That(Knows(hero, "CFHaste"), Is.False, "rank 3 waits for level 5");

            LevelTo(hero, 5);
            Assert.That(book.Slots, Is.EqualTo(new[] { 5, 3, 3, 2 }));
            Assert.That(Knows(hero, "CFFireball"), Is.True);
            Assert.That(book.Expended.Count, Is.GreaterThanOrEqualTo(book.Slots.Count));
        });
    }

    [Test]
    public async Task ASpontaneousCasterGainsSlotsThatCanBeSpent()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Caster("Sorcerer");
            var book = Book(hero);
            Assert.That(book.Slots, Is.EqualTo(new[] { 5, 3 }));
            Assert.That(book.Prepared, Is.False);

            LevelTo(hero, 3);
            Assert.That(book.Slots, Is.EqualTo(new[] { 5, 4, 3 }));
            Assert.That(Knows(hero, "CFBlur"), Is.True);

            var spell = SEntMan.System<CFSpellSystem>();
            var blur = SEntMan.System<SharedActionsSystem>().GetActions(hero)
                .First(a => SEntMan.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID == "CFBlur");
            var comp = SEntMan.GetComponent<CFSpellComponent>(blur);
            Assert.That(spell.HasSlot(hero, comp), Is.True);

            book.Expended[2] = 3;
            Assert.That(spell.HasSlot(hero, comp), Is.False, "a rank 2 spell uses the rank 2 slots");
        });
    }

    [Test]
    public async Task DivineFontsExtraSlotsSurviveLevellingUp()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Caster("Cleric", new List<CFSpellPick>());
            var book = Book(hero);
            var font = book.Slots[1] - 2;
            Assert.That(font, Is.GreaterThan(0));

            LevelTo(hero, 3);
            Assert.That(book.Slots[1], Is.EqualTo(3 + font));
            Assert.That(book.Slots[2], Is.EqualTo(2));
            Assert.That(book.PreparableSlots, Is.EqualTo(new[] { 5, 3, 2 }));
        });
    }

    [Test]
    public async Task AnimistApparitionSlotsFollowTheirOwnTable()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Caster("Animist", new List<CFSpellPick>());
            var book = Book(hero);
            Assert.That(CFApparitions.Total(book, 1), Is.EqualTo(1));

            LevelTo(hero, 2);
            Assert.That(CFApparitions.Total(book, 1), Is.EqualTo(1));

            LevelTo(hero, 5);
            Assert.Multiple(() =>
            {
                Assert.That(CFApparitions.Total(book, 1), Is.EqualTo(1));
                Assert.That(CFApparitions.Total(book, 2), Is.EqualTo(1));
                Assert.That(CFApparitions.Total(book, 3), Is.EqualTo(1));
            });
        });
    }

    [Test]
    public async Task ASpellAboveItsSlotIsLeftOut()
    {
        await Server.WaitAssertion(() =>
        {
            // A client could send a rank 3 spell in a rank 1 slot.
            var picks = new List<CFSpellPick> { Pick(1, 0, "Fireball"), Pick(1, 1, "Sleep") };
            var within = CFSpellSlots.Within(picks, new[] { 5, 2 }, Server.ProtoMan);
            Assert.That(within.Select(p => p.Spell.Id), Is.EquivalentTo(new[] { "Sleep" }));
        });
    }
}
