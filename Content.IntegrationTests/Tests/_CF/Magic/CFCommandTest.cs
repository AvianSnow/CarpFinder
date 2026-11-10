using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;
using Content.Server.CF.GOAP;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Spells;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Magic;

/// <summary>Command's five orders, picked from its menu, and that both kinds of AI obey them.</summary>
[TestFixture]
[TestOf(typeof(CFCommandSystem))]
public sealed class CFCommandTest : CFGOAPLoggedTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: [ MobHuman, CFGOAPBrain ]
  id: CFCommandTestBoss
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Monster

- type: entity
  parent: MobHuman
  id: CFCommandTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 1
    maxHp: 1000
    currentHp: 1000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

# Fixed numbers, so rebalancing the boar does not move this test. The test takes its brain out to make it stock HTN.
- type: entity
  parent: CFMobBoar
  id: CFCommandTestBoar
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 100
    currentHp: 100
";

    private CFDevMap _map = default!;

    private CFCommandSystem Command => SEntMan.System<CFCommandSystem>();

    private EntityUid Spawn(string id, int x)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private float Distance(EntityUid a, EntityUid b)
    {
        var transform = SEntMan.System<SharedTransformSystem>();
        return Vector2.Distance(transform.GetWorldPosition(a), transform.GetWorldPosition(b));
    }

    [TestCase(1, CFCommandOrder.Approach)]
    [TestCase(4, CFCommandOrder.Flee)]
    [TestCase(5, CFCommandOrder.Halt)]
    public async Task TheMenuPickIsTheOrder(byte mode, CFCommandOrder order)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFCommandTestHero", 0);
            var target = Spawn("CFCommandTestHero", 2);
            var spell = SEntMan.SpawnEntity("CFCommand", _map.Local(0, 0));
            SEntMan.GetComponent<CFModeChoiceComponent>(spell).Chosen = mode;

            new CFCommandEffect().Apply(SEntMan, new CFEffectArgs { User = hero, Source = spell, Target = target });

            Assert.That(SEntMan.GetComponent<CFCommandedComponent>(target).Order, Is.EqualTo(order));
        });
    }

    [Test]
    public async Task CommandOffersAllFiveWords()
    {
        await Server.WaitAssertion(() =>
        {
            var proto = Server.ProtoMan.Index<EntityPrototype>("CFCommand");
            Assert.That(proto.TryComp<CFModeChoiceComponent>(out var choice, SEntMan.ComponentFactory));
            var values = new List<byte>();
            foreach (var mode in choice!.Modes)
            {
                values.Add(mode.Value);
            }

            Assert.That(values, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        });
    }

    [Test]
    public async Task DropLetsGoAndFallKnocksProne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var hero = Spawn("CFCommandTestHero", 0);
            var target = Spawn("CFCommandTestHero", 2);
            var sword = Spawn("CFLongsword", 2);
            Assert.That(hands.TryPickupAnyHand(target, sword, checkActionBlocker: false));

            Command.Give(hero, target, CFCommandOrder.Drop, 6f, 15);
            Assert.That(hands.IsHolding(target, sword), Is.False, "Drop");

            Command.Give(hero, target, CFCommandOrder.Fall, 6f, 15);
            Assert.That(SEntMan.System<CFConditionSystem>().IsProne(target), "Fall");
            Assert.That(SEntMan.HasComponent<CFCommandedComponent>(target), Is.False, "Neither order lasts.");
        });
    }

    [Test]
    public async Task ACreatureThatDoesNotUnderstandIgnoresIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFCommandTestHero", 0);
            var languages = SEntMan.AddComponent<CFLanguagesComponent>(hero);
            languages.Known = new List<ProtoId<CFLanguagePrototype>> { "Common" };
            languages.Speaking = "Common";

            // Animals only know Bestial.
            var boar = Spawn("CFCommandTestBoar", 3);
            Command.Give(hero, boar, CFCommandOrder.Halt, 6f, 15);

            Assert.That(SEntMan.HasComponent<CFCommandedComponent>(boar), Is.False);
        });
    }

    [Test]
    public async Task ABrainPlansToObeyAheadOfFighting()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Spawn("CFCommandTestBoss", 0);
            var goap = SEntMan.GetComponent<CFGOAPComponent>(boss);
            var hero = Spawn("CFCommandTestHero", 4);
            var goapSystem = SEntMan.System<CFGOAPSystem>();
            goapSystem.Start((boss, goap));

            Command.Give(hero, boss, CFCommandOrder.Halt, 6f, 15);
            goapSystem.Think((boss, goap));

            Assert.That(goap.Goal?.Id, Is.EqualTo("Obey"));
            Assert.That(goap.Plan, Has.Count.EqualTo(1));
            Assert.That(goap.Plan[0].Id, Is.EqualTo("Obey"));
        });
    }

    /// <summary>A brain told to halt stays put beside an enemy it would otherwise walk up to, then fights again.</summary>
    [Test]
    public async Task ABrainHaltsThenGoesBackToFighting()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var config = Server.ResolveDependency<IConfigurationManager>();
        EntityUid boss = default;
        EntityUid hero = default;
        var start = 0f;

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            boss = Spawn("CFCommandTestBoss", 0);
            hero = Spawn("CFCommandTestHero", 5);
            Command.Give(hero, boss, CFCommandOrder.Halt, 3f, 15);
            start = Distance(boss, hero);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(boss);
            SEntMan.System<NPCSystem>().WakeNPC(boss);
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Distance(boss, hero), Is.EqualTo(start).Within(0.5f), "It halted.");
        });

        await Pair.RunTicksSync(150);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CFCommandedComponent>(boss), Is.False, "The order ran out.");
            Assert.That(Distance(boss, hero), Is.LessThan(start - 1f), "It went back to its own plan.");
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    /// <summary>A stock HTN boar runs from the caster it would otherwise charge, and gets its HTN back afterwards.</summary>
    [Test]
    public async Task AStockMobFleesOnGoapSteering()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var config = Server.ResolveDependency<IConfigurationManager>();
        EntityUid boar = default;
        EntityUid hero = default;
        var start = 0f;

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            hero = Spawn("CFCommandTestHero", 0);
            boar = Spawn("CFCommandTestBoar", 3);
            CFStockAi.MakeStock(SEntMan, boar);
            SEntMan.RemoveComponent<CFLanguagesComponent>(boar);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(boar);
            SEntMan.System<NPCSystem>().WakeNPC(boar);
            Command.Give(hero, boar, CFCommandOrder.Flee, 4f, 15);
            start = Distance(boar, hero);
            Assert.That(SEntMan.GetComponent<HTNComponent>(boar).Enabled, Is.False, "Its HTN sleeps for the order.");
        });

        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Distance(boar, hero), Is.GreaterThan(start + 1f), "It ran.");
        });

        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<CFCommandedComponent>(boar), Is.False);
            Assert.That(SEntMan.GetComponent<HTNComponent>(boar).Enabled, "Its HTN is back.");
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }
}
