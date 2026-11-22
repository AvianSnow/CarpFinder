using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Wizard's school spells, Drain Bonded Item and the theses.</summary>
[TestFixture]
[TestOf(typeof(CFClassChoicePrototype))]
public sealed class CFWizardSchoolTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFWizardTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFWizardTestHero
  id: CFWizardTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private EntityUid Spawn(int x, string id = "CFWizardTestHero")
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private bool HasAction(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    [Test]
    public async Task EverySchoolGivesItsSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var missing = new List<string>();
        await Server.WaitPost(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>())
            {
                if (choice.Class != "Wizard" || !choice.ID.StartsWith("School"))
                    continue;

                var wizard = Spawn(0);
                SEntMan.System<CFCharacterFeatureSystem>().ApplyClassChoice(wizard, choice, 1);
                var focus = choice.Grants.OfType<Content.Shared.CF.Character.Grants.CFGrantFocusSpell>().FirstOrDefault();
                if (focus == null || !HasAction(wizard, "CF" + focus.Spell.Id))
                    missing.Add(choice.ID);
            }
        });

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public async Task EarthworksGrowsWithItsActionsAndWardsWithItsPulses()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int one = 0, three = 0, ward = 0, grown = 0, capped = 0;
        await Server.WaitPost(() =>
        {
            var wizard = Spawn(0);
            var spellshapes = SEntMan.System<CFSpellshapeSystem>();
            var earthworks = SEntMan.SpawnEntity("CFEarthworks", MapCoordinates.Nullspace);
            var area = SEntMan.GetComponent<CFAreaEffectComponent>(earthworks);
            var mode = SEntMan.GetComponent<CFModeChoiceComponent>(earthworks);
            mode.Chosen = 1;
            one = spellshapes.AreaFeet(earthworks, wizard, area);
            mode.Chosen = 3;
            three = spellshapes.AreaFeet(earthworks, wizard, area);

            var wards = SEntMan.SpawnEntity("CFProtectiveWards", MapCoordinates.Nullspace);
            var wardArea = SEntMan.GetComponent<CFAreaEffectComponent>(wards);
            ward = spellshapes.AreaFeet(wards, wizard, wardArea);
            var cast = new CFSustainedCast { Action = wards, Pulses = 2 };
            SEntMan.EnsureComponent<CFSustainedCastsComponent>(wizard).Casts.Add(cast);
            grown = spellshapes.AreaFeet(wards, wizard, wardArea);
            cast.Pulses = 20;
            capped = spellshapes.AreaFeet(wards, wizard, wardArea);
        });

        Assert.Multiple(() =>
        {
            Assert.That(one, Is.EqualTo(5));
            Assert.That(three, Is.EqualTo(15));
            Assert.That(ward, Is.EqualTo(5));
            Assert.That(grown, Is.EqualTo(15));
            Assert.That(capped, Is.EqualTo(30));
        });
    }

    [Test]
    public async Task CharmingPushAndHandOfTheApprentice()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int penalty = 0, hp = 0;
        bool emptyRefused = false;
        await Server.WaitPost(() =>
        {
            var wizard = Spawn(0);
            var foe = Spawn(1, "CFWizardTestFoe");
            SEntMan.System<CFConditionSystem>().Overextend(foe, wizard, once: false, penalty: 1);
            penalty = SEntMan.System<CFConditionSystem>().CircumstanceAttackPenalty(foe, wizard);

            var hand = SEntMan.SpawnEntity("CFHandOfTheApprentice", MapCoordinates.Nullspace);
            var attempt = new ActionAttemptEvent(wizard, null);
            SEntMan.EventBus.RaiseLocalEvent(hand, ref attempt);
            emptyRefused = attempt.Cancelled;

            var sword = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(wizard, sword);
            var args = new CFEffectArgs
            {
                User = wizard,
                Source = hand,
                Target = foe,
                Defense = Content.Shared.CF.Checks.CFDefenseType.ArmorClass,
                Degree = Content.Shared.CF.Checks.CFDegree.Success,
            };
            new CFHurledWeaponEffect().Apply(SEntMan, args);
            hp = SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(penalty, Is.EqualTo(-1));
            Assert.That(emptyRefused, Is.True, "No melee weapon, no spell.");
            Assert.That(hp, Is.InRange(92, 99), "A longsword's d8, with no modifier.");
        });
    }

    [Test]
    public async Task DrainBondedItemBringsBackOneSpentSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool before = true, during = false, spent = false, after = true;
        await Server.WaitPost(() =>
        {
            var wizard = Spawn(0);
            var book = SEntMan.EnsureComponent<CFSpellbookComponent>(wizard);
            book.Prepared = true;
            book.Slots = new List<int> { 0, 1 };
            book.Expended = new List<int> { 0, 1 };
            book.PreparedSlots = new List<CFPreparedSlot> { new("Sleep", 1, spent: true) };
            book.Repertoire.Add("Sleep");

            var spells = SEntMan.System<CFSpellSystem>();
            var sleep = SEntMan.GetComponent<CFSpellComponent>(SEntMan.SpawnEntity("CFSleep", MapCoordinates.Nullspace));
            before = spells.HasSlot(wizard, sleep);
            spells.DrainBondedItem(wizard);
            during = spells.HasSlot(wizard, sleep);
            spent = spells.TrySpend(wizard, sleep);
            after = spells.HasSlot(wizard, sleep);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(during, Is.True);
            Assert.That(spent, Is.True);
            Assert.That(after, Is.False, "The drain covers one cast.");
        });
    }

    [Test]
    public async Task ExperimentalSpellshapingOffersOnlySpellshapes()
    {
        await Server.WaitAssertion(() =>
        {
            var thesis = Server.ProtoMan.Index(new ProtoId<CFClassChoicePrototype>("ThesisExperimentalSpellshaping"));
            var build = new CFCharacterBuildData { Class = "Wizard" };
            var offered = CFOptionChoices.Allowed(Server.ProtoMan, thesis.Choices[0], build);
            Assert.That(offered, Is.Not.Empty);
            foreach (var id in offered)
            {
                Assert.That(Server.ProtoMan.Index<CFFeatPrototype>(id).Traits, Does.Contain("Spellshape"), id);
            }
        });
    }
}
