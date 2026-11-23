using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Monk's qi spells and the bonuses its stances were missing.</summary>
[TestFixture]
[TestOf(typeof(CFInfuseStrikesEffect))]
public sealed class CFMonkTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<CFFeatPrototype> QiSpells = "CFQiSpells";
    private static readonly ProtoId<CFFeatPrototype> HarmonizeSelf = "CFHarmonizeSelf";
    private static readonly EntProtoId InnerUpheaval = "CFInnerUpheaval";

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFMonkTestHero
  components:
  - type: CFCombatant
    level: 3
    maxHp: 500
    currentHp: 500
    trainedSkills:
    - Deception
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private EntityUid Spawn()
    {
        return SEntMan.SpawnEntity("CFMonkTestHero", _map.Local(0, 0));
    }

    private bool HasAction(EntityUid hero, string id)
    {
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private void Enter(EntityUid hero, string stance)
    {
        Features.GrantAction(hero, stance);
        var actions = SEntMan.System<SharedActionsSystem>();
        var action = actions.GetActions(hero)
            .First(a => SEntMan.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID == stance);
        var attempt = new ActionAttemptEvent(hero, null);
        SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
        if (!attempt.Cancelled)
            actions.PerformAction(hero, action);
    }

    [Test]
    public async Task QiSpellsGivesTheSpellPickedAndHarmonizeSelfItsOwn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool rush = false, upheaval = true, harmonize = false;
        var focus = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var xp = SEntMan.EnsureComponent<CFExperienceComponent>(hero);
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFQiSpells", Slot = 0, Value = "pick:QiRush" });
            Features.ApplyFeat(hero, Server.ProtoMan.Index(QiSpells));
            Features.ApplyFeat(hero, Server.ProtoMan.Index(HarmonizeSelf));
            rush = HasAction(hero, "CFQiRush");
            upheaval = HasAction(hero, "CFInnerUpheaval");
            harmonize = HasAction(hero, "CFHarmonizeSelf");
            focus = SEntMan.GetComponent<CFSpellbookComponent>(hero).FocusPoints;
        });

        Assert.Multiple(() =>
        {
            Assert.That(rush, Is.True);
            Assert.That(upheaval, Is.False);
            Assert.That(harmonize, Is.True);
            Assert.That(focus, Is.EqualTo(2), "Two qi spells, two Focus Points");
        });
    }

    [Test]
    public async Task InnerUpheavalChargesTheNextStrikes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int dice = 0, attack = 0;
        string type = null;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            if (!Server.ProtoMan.Index(InnerUpheaval).TryComp<CFDegreeEffectsComponent>(out var effects, SEntMan.ComponentFactory))
                return;

            SEntMan.System<CFEffectSystem>().Run(effects.Landed, new CFEffectArgs
            {
                User = hero,
                Source = hero,
                Target = hero,
                Position = SEntMan.GetComponent<TransformComponent>(hero).Coordinates,
            });
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            dice = options.InfusionDice;
            type = options.InfusionType;
            attack = SEntMan.System<CFBonusSystem>().TotalOfType(hero, CFBonusStat.AttackRoll, CFBonusType.Status);
        });

        Assert.Multiple(() =>
        {
            Assert.That(dice, Is.EqualTo(1));
            Assert.That(type, Is.EqualTo("Force"));
            Assert.That(attack, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StancesGiveTheirBonuses()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int dc = 0, maneuvers = 0, deception = 0, mountain = 0, plain = 0;
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            var petal = Spawn();
            Enter(petal, "CFActTwistingPetalStance");
            dc = bonuses.Total(petal, CFBonusStat.ManeuverDc);
            maneuvers = bonuses.Total(petal, CFBonusStat.Maneuvers);
            deception = bonuses.Total(petal, CFBonusStat.SkillChecks, skill: "Deception");
            plain = bonuses.Total(petal, CFBonusStat.SkillChecks);

            var stone = Spawn();
            Enter(stone, "CFActMountainStance");
            mountain = bonuses.Total(stone, CFBonusStat.ManeuverDc);
        });

        Assert.Multiple(() =>
        {
            Assert.That(dc, Is.EqualTo(2));
            Assert.That(maneuvers, Is.EqualTo(1));
            Assert.That(deception, Is.EqualTo(1));
            Assert.That(plain, Is.EqualTo(0), "Only Deception");
            Assert.That(mountain, Is.EqualTo(2));
        });
    }
}
