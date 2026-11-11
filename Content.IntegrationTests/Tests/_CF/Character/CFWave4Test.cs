using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Barbarian, Champion, Monk, Swashbuckler, Investigator, Alchemist, Sorcerer and Oracle, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFWhileBonusSystem))]
public sealed class CFWave4Test : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);
    private static readonly string[] Classes = { "Barbarian", "Champion", "Swashbuckler", "Investigator", "Alchemist", "Sorcerer", "Oracle" };
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFWave4Hero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFWave4Hero
  id: CFWave4Foe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private CFCombatOptionsComponent Options(EntityUid mob) => SEntMan.GetComponent<CFCombatOptionsComponent>(mob);

    private EntityUid Spawn(string id = "CFWave4Hero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private void GiveFeature(EntityUid hero, string cls, string name)
    {
        var feature = Server.ProtoMan.Index<CFClassPrototype>(cls).Features.First(f => f.Name == name);
        Features.Apply(hero, feature.Grants, $"feature:{name}");
    }

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index<CFFeatPrototype>(id).Grants, $"feat:{id}");
    }

    private void GiveChoice(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, $"choice:{id}");
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool CanUse(EntityUid hero, EntityUid action, EntityUid target)
    {
        var attempt = new ActionAttemptEvent(hero, target);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        return !attempt.Cancelled;
    }

    private int Speed(EntityUid mob) => Bonuses.Total(mob, CFBonusStat.Speed);

    /// <summary>The rage bonus comes and goes with the rage, and does not stack with the lasting +5.</summary>
    [Test]
    public async Task FuriousFootfallsIsFasterWhileRaging()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        int calm = 0, raging = 0, calmAgain = 0;
        await Server.WaitPost(() =>
        {
            hero = Spawn();
            GiveFeature(hero, "Barbarian", "Furious Footfalls");
            calm = Speed(hero);
            Options(hero).Raging = true;
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            raging = Speed(hero);
            Options(hero).Raging = false;
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() => calmAgain = Speed(hero));

        Assert.Multiple(() =>
        {
            Assert.That(calm, Is.EqualTo(5));
            Assert.That(raging, Is.EqualTo(10));
            Assert.That(calmAgain, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task IncredibleMovementNeedsNoArmor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var speed = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Monk", "Incredible Movement");
            speed = Speed(hero);
        });

        Assert.That(speed, Is.EqualTo(10));
    }

    [Test]
    public async Task StylishCombatantNeedsPanache()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        int without = 0, with = 0;
        await Server.WaitPost(() =>
        {
            hero = Spawn();
            GiveFeature(hero, "Swashbuckler", "Stylish Combatant");
            without = Speed(hero);
            Options(hero).Panache = true;
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() => with = Speed(hero));

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.EqualTo(0));
            Assert.That(with, Is.EqualTo(5));
        });
    }

    /// <summary>A bonus against a trait only counts against that trait, and only while raging.</summary>
    [Test]
    public async Task DraconicArroganceOnlyCountsWhileRagingAgainstEmotion()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        var emotion = new HashSet<ProtoId<CFTraitPrototype>> { "Emotion" };
        int calm = 0, angry = 0, other = 0;
        await Server.WaitPost(() =>
        {
            hero = Spawn();
            GiveFeat(hero, "CFDraconicArrogance");
            calm = Bonuses.Total(hero, CFBonusStat.Saves, traits: emotion);
            Options(hero).Raging = true;
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            angry = Bonuses.Total(hero, CFBonusStat.Saves, traits: emotion);
            other = Bonuses.Total(hero, CFBonusStat.Saves);
        });

        Assert.Multiple(() =>
        {
            Assert.That(calm, Is.EqualTo(0));
            Assert.That(angry, Is.EqualTo(2));
            Assert.That(other, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task InstinctsChangeRage()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, fury = 0, dragon = 0, calmDie = 0, ragingDie = 0;
        await Server.WaitPost(() =>
        {
            var strikes = SEntMan.System<CFStrikeSystem>();
            plain = Options(Spawn()).RageDamage;

            var furious = Spawn();
            GiveChoice(furious, "InstinctFury");
            fury = Options(furious).RageDamage;

            var draconic = Spawn();
            GiveChoice(draconic, "InstinctDragon");
            dragon = Options(draconic).RageDamage;

            var animal = Spawn();
            GiveChoice(animal, "InstinctAnimal");
            calmDie = strikes.UnarmedStats(animal).DamageDie;
            Options(animal).Raging = true;
            ragingDie = strikes.UnarmedStats(animal).DamageDie;
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(2));
            Assert.That(fury, Is.EqualTo(3));
            Assert.That(dragon, Is.EqualTo(4));
            Assert.That(calmDie, Is.EqualTo(4));
            Assert.That(ragingDie, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task AFinisherActionNeedsPanacheAndSpendsIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool without = true, with = false, after = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var foe = Spawn("CFWave4Foe", 1);
            GiveFeature(hero, "Swashbuckler", "Panache");
            GiveFeat(hero, "CFUnbalancingFinisher");
            var action = Action(hero, "CFActUnbalancingFinisher");

            without = CanUse(hero, action, foe);
            Options(hero).Panache = true;
            with = CanUse(hero, action, foe);
            SEntMan.System<SharedActionsSystem>().PerformAction(hero, action, new CFActivityTargetEvent { Target = foe });
            after = Options(hero).Panache;
        });

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.False);
            Assert.That(with, Is.True);
            Assert.That(after, Is.False);
        });
    }

    [Test]
    public async Task OpportuneRiposteUsesTheReaction()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = -1, after = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var foe = Spawn("CFWave4Foe", 1);
            GiveFeature(hero, "Swashbuckler", "Opportune Riposte");
            before = Options(hero).ReactionSpent;
            SEntMan.System<CFReactionSystem>().AnswerCriticalMiss(hero, foe);
            after = Options(hero).ReactionSpent;
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(0));
            Assert.That(after, Is.Not.EqualTo(0));
        });
    }

    /// <summary>Heal at rank 1 is 1d8+8, so only Sorcerous Potency can reach 17.</summary>
    [Test]
    public async Task SorcerousPotencyAddsTheRank()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, potent = 0;
        await Server.WaitPost(() =>
        {
            int Most(EntityUid caster)
            {
                var effects = SEntMan.System<CFEffectSystem>();
                var spell = SEntMan.SpawnEntity("CFHeal", MapCoordinates.Nullspace);
                var patient = Spawn();
                var combat = SEntMan.GetComponent<CFCombatantComponent>(patient);
                var most = 0;
                for (var i = 0; i < 80; i++)
                {
                    combat.CurrentHp = 100;
                    effects.ResolveTarget(spell, caster, patient);
                    most = Math.Max(most, combat.CurrentHp - 100);
                }

                return most;
            }

            plain = Most(Spawn());
            var sorcerer = Spawn();
            GiveFeature(sorcerer, "Sorcerer", "Sorcerous Potency");
            potent = Most(sorcerer);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.LessThanOrEqualTo(16));
            Assert.That(potent, Is.GreaterThan(16));
        });
    }

    [Test]
    public async Task ABloodlineSetsTheTradition()
    {
        await Server.WaitAssertion(() =>
        {
            var build = new CFCharacterBuildData { Class = "Sorcerer" };
            Assert.That(CFClassChoices.Tradition(Server.ProtoMan, build, CFSpellTradition.Arcane), Is.EqualTo(CFSpellTradition.Arcane));
            build.ClassChoices.Add("BloodlineAngelic");
            Assert.That(CFClassChoices.Tradition(Server.ProtoMan, build, CFSpellTradition.Arcane), Is.EqualTo(CFSpellTradition.Divine));
        });
    }

    /// <summary>Strength 40 makes the Grapple all but certain and the damage easy to see.</summary>
    [Test]
    public async Task CrushingGrabHurtsOnAGrapple()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var worst = StartHp;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var foe = Spawn("CFWave4Foe", 1);
            GiveFeat(hero, "CFCrushingGrab");
            var maneuvers = SEntMan.System<CFManeuverSystem>();
            for (var i = 0; i < 10 && worst == StartHp; i++)
            {
                maneuvers.TryGrapple(hero, foe);
                worst = SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;
            }
        });

        Assert.That(worst, Is.LessThan(StartHp));
    }

    [Test]
    public async Task EffectActionsWork()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int armor = 0, healed = 0;
        await Server.WaitPost(() =>
        {
            var actions = SEntMan.System<SharedActionsSystem>();
            var hero = Spawn();
            GiveFeat(hero, "CFExtravagantParry");
            actions.PerformAction(hero, Action(hero, "CFActExtravagantParry"), new CFActivityInstantEvent());
            armor = Bonuses.Total(hero, CFBonusStat.ArmorClass);

            var oracle = Spawn();
            var patient = Spawn("CFWave4Hero", 1);
            GiveFeat(oracle, "CFNudgetheScales");
            var combat = SEntMan.GetComponent<CFCombatantComponent>(patient);
            combat.CurrentHp = 100;
            actions.PerformAction(oracle, Action(oracle, "CFActNudgeScales"), new CFActivityTargetEvent { Target = patient });
            healed = combat.CurrentHp - 100;
        });

        Assert.Multiple(() =>
        {
            Assert.That(armor, Is.EqualTo(1));
            Assert.That(healed, Is.EqualTo(4));
        });
    }

    /// <summary>A wrong id in a choice's grants, or a spell with no action, only shows when the grant runs.</summary>
    [Test]
    public async Task EveryNewSubclassOptionCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => Classes.Contains(c.Class.Id)))
            {
                GiveChoice(Spawn(), choice.ID);
                count++;
            }
        });

        Assert.That(count, Is.GreaterThanOrEqualTo(50));
    }

    /// <summary>A grant that names a missing action or spell only fails when it runs.</summary>
    [Test]
    public async Task EveryFeatOfTheseClassesCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class is "Barbarian" or "Champion" or "Monk" or "Swashbuckler" or "Investigator" or "Alchemist" or "Sorcerer" or "Oracle"))
            {
                GiveFeat(Spawn(), feat.ID);
                count++;
            }
        });

        Assert.That(count, Is.GreaterThanOrEqualTo(170));
    }

    [Test]
    public async Task EveryRequiredSubclassExistsForItsClass()
    {
        var wrong = new List<string>();
        await Server.WaitAssertion(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class != null && Classes.Contains(f.Class)))
            {
                foreach (var id in feat.RequiresChoice)
                {
                    if (!Server.ProtoMan.TryIndex(id, out var choice) || choice.Class.Id != feat.Class)
                        wrong.Add($"{feat.ID}: {id}");
                }
            }
        });

        Assert.That(wrong, Is.Empty);
    }

    [Test]
    public async Task LayOnHandsIsAFocusSpell()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hasAction = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveChoice(hero, "DevotionLayOnHands");
            hasAction = SEntMan.System<SharedActionsSystem>().GetActions(hero)
                .Any(action => SEntMan.TryGetComponent(action, out CFSpellComponent spell) && spell.Focus);
        });

        Assert.That(hasAction, Is.True);
    }
}
