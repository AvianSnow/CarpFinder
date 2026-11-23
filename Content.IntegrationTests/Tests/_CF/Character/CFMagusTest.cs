using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Magus: conflux spells that are Strikes, and what a hybrid study adds to Arcane Cascade.</summary>
[TestFixture]
[TestOf(typeof(CFMagusSystem))]
public sealed class CFMagusTest : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<CFClassChoicePrototype> SparklingTarge = "StudySparklingTarge";
    private static readonly ProtoId<CFClassChoicePrototype> InexorableIron = "StudyInexorableIron";
    private static readonly ProtoId<CFClassChoicePrototype> LaughingShadow = "StudyLaughingShadow";

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFMagusTestHero
  components:
  - type: CFCombatant
    level: 4
    armorClass: 1
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFMagusTestHero
  id: CFMagusTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private EntityUid Spawn(string id = "CFMagusTestHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private EntityUid Magus(ProtoId<CFClassChoicePrototype> study)
    {
        var hero = Spawn();
        Features.Apply(hero, Server.ProtoMan.Index(study).Grants, $"choice:{study.Id}");
        return hero;
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        Features.GrantAction(hero, id);
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(a => SEntMan.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID == id);
    }

    private bool Use(EntityUid hero, Entity<ActionComponent> action, EntityUid? target)
    {
        var attempt = new ActionAttemptEvent(hero, target);
        SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
        if (attempt.Cancelled)
            return false;

        if (target is { } aimed)
            SEntMan.System<SharedActionsSystem>().PerformAction(hero, action, new CFActivityTargetEvent { Target = aimed });
        else
            SEntMan.System<SharedActionsSystem>().PerformAction(hero, action);

        return true;
    }

    [Test]
    public async Task SevenStudiesGiveAConfluxStrike()
    {
        await Server.WaitAssertion(() =>
        {
            var seen = 0;
            foreach (var study in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>())
            {
                if (study.Class != "Magus")
                    continue;

                foreach (var grant in study.Grants.OfType<CFGrantAction>())
                {
                    Assert.That(Server.ProtoMan.TryIndex<EntityPrototype>(grant.Action, out var action), $"{study.ID}: no {grant.Action}");
                    Assert.That(action.TryComp<CFStrikeActionComponent>(out var strike, SEntMan.ComponentFactory), study.ID);
                    Assert.That(strike.FocusPoints, Is.EqualTo(1), study.ID);
                    Assert.That(strike.RechargesSpellstrike, Is.True, study.ID);
                    Assert.That(study.Grants.OfType<CFGrantFocusPoints>().Any(), $"{study.ID} has no Focus Point for it");
                    seen++;
                }
            }

            Assert.That(seen, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task AConfluxStrikeSpendsAFocusPointAndIsRefusedWithout()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        bool first = false, second = true;
        int spent = 0, hurt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Magus(SparklingTarge);
            var foe = Spawn("CFMagusTestFoe", 1);
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, Spawn("CFLongsword"));
            var strike = Action(hero, "CFActShieldingStrike");
            first = Use(hero, strike, foe);
            spent = SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusSpent;
            hurt = StartHp - SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;
            second = Use(hero, strike, foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(spent, Is.EqualTo(1));
            Assert.That(second, Is.False, "No Focus Point left");
            Assert.That(hurt, Is.GreaterThanOrEqualTo(0));
        });
    }

    [Test]
    public async Task ArcaneCascadeGivesTheStudysBenefit()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        int tempWithSword = 0, tempBare = 0, speed = 0, speedAfter = -1, burned = 0;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            var hands = SEntMan.System<SharedHandsSystem>();

            // Inexorable Iron: only with a two-handed weapon. Level 4 is 2 temporary Hit Points.
            var bare = Magus(InexorableIron);
            Use(bare, Action(bare, "CFActArcaneCascade"), null);
            tempBare = SEntMan.GetComponent<CFCombatantComponent>(bare).TemporaryHp;

            var iron = Magus(InexorableIron);
            hands.TryPickupAnyHand(iron, Spawn("CFGreatsword"));
            Use(iron, Action(iron, "CFActArcaneCascade"), null);
            tempWithSword = SEntMan.GetComponent<CFCombatantComponent>(iron).TemporaryHp;

            // Laughing Shadow, with Burning Cascade: faster in the stance, and the enemy beside it is burned.
            var shadow = Magus(LaughingShadow);
            Features.SetOption(shadow, CFCombatOption.BurningCascade);
            var foe = Spawn("CFMagusTestFoe", 1);
            var cascade = Action(shadow, "CFActArcaneCascade");
            Use(shadow, cascade, null);
            speed = bonuses.TotalOfType(shadow, CFBonusStat.Speed, CFBonusType.Status);
            burned = StartHp - SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;

            SEntMan.System<CFStanceSystem>().Leave(shadow);
            speedAfter = bonuses.TotalOfType(shadow, CFBonusStat.Speed, CFBonusType.Status);
        });

        Assert.Multiple(() =>
        {
            Assert.That(tempBare, Is.EqualTo(0));
            Assert.That(tempWithSword, Is.EqualTo(2));
            Assert.That(speed, Is.EqualTo(10), "Unarmored: +10 feet");
            Assert.That(burned, Is.EqualTo(1));
            Assert.That(speedAfter, Is.EqualTo(0), "It ends with the stance");
        });
    }
}
