using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Influence;
using Content.Server.CF.GOAP.Nodes;
using Content.Server.CF.GOAP.Steering;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Items;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>What advanced NPCs decide, and that a real one acts on it.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPSystem))]
public sealed class CFGOAPTest : CFGOAPLoggedTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: [ MobHuman, CFGOAPBrain ]
  id: CFGOAPTestBoss
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
  id: CFGOAPTestHero
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
";

    private CFDevMap _map = default!;

    private CFGOAPSystem Goap => SEntMan.System<CFGOAPSystem>();
    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private Entity<CFGOAPComponent> Boss(int x = 0, string profile = "Default", bool armed = true)
    {
        var boss = SEntMan.SpawnEntity("CFGOAPTestBoss", _map.Local(x, 0));
        var goap = SEntMan.GetComponent<CFGOAPComponent>(boss);
        goap.Profile = profile;
        if (armed)
            Give(boss, "CFLongsword");

        Goap.Start((boss, goap));
        return (boss, goap);
    }

    private EntityUid Hero(int x)
    {
        return SEntMan.SpawnEntity("CFGOAPTestHero", _map.Local(x, 0));
    }

    private EntityUid Give(EntityUid mob, string item)
    {
        var uid = SEntMan.SpawnEntity(item, _map.Local(0, 0));
        Assert.That(Hands.TryPickupAnyHand(mob, uid, checkActionBlocker: false));
        return uid;
    }

    private void SetHealth(EntityUid mob, int hp)
    {
        SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp = hp;
    }

    private static List<string> Plan(Entity<CFGOAPComponent> boss)
    {
        var plan = new List<string>();
        foreach (var step in boss.Comp.Plan)
        {
            plan.Add(step);
        }

        return plan;
    }

    [Test]
    public async Task ItClosesOnAndStrikesWhatItCanSee()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            var hero = Hero(4);

            Goap.Think(boss);
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("KillTarget"));
            Assert.That(boss.Comp.Target, Is.EqualTo(hero));
            Assert.That(Plan(boss), Is.EqualTo(new[] { "Close", "Strike" }));
        });
    }

    [Test]
    public async Task LowHealthWithAPotionDrinksIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            Hero(4);
            Give(boss, "CFHealingPotionMinor");
            SetHealth(boss, 40);

            Goap.Think(boss);
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("Heal"));
            Assert.That(Plan(boss), Is.EqualTo(new[] { "DrinkHealing" }));
        });
    }

    [Test]
    public async Task AnUnarmedNpcGetsAWeaponFirst()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss(armed: false);
            Hero(5);
            SEntMan.SpawnEntity("CFLongsword", _map.Local(2, 0));

            Goap.Think(boss);
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("ArmSelf"));
            Assert.That(Plan(boss), Is.EqualTo(new[] { "PickUpWeapon" }));
        });
    }

    [TestCase("Cowardly", 25, "StayAlive")]
    [TestCase("Cowardly", 80, "KillTarget")]
    [TestCase("Brave", 25, "KillTarget")]
    public async Task APersonalitySetsWhenItRuns(string profile, int hp, string goal)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss(profile: profile);
            Hero(4);
            SetHealth(boss, hp);

            Goap.Think(boss);
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo(goal));
        });
    }

    [Test]
    public async Task AnUndetectedCreatureIsNotSeen()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            var hero = Hero(3);
            var stealth = SEntMan.EnsureComponent<CFStealthComponent>(hero);
            stealth.Active = true;
            stealth.Detection[boss] = CFDetection.Undetected;

            Goap.Think(boss);
            Assert.That(boss.Comp.Target, Is.Null, "It may not know what it cannot perceive.");
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("Idle"));
        });
    }

    [Test]
    public async Task ItHuntsFromTheLastKnownPosition()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            var hero = Hero(5);
            var node = SEntMan.SpawnEntity("CFInvestigationNode", _map.Local(6, 0));

            Goap.Think(boss);
            Assert.That(boss.Comp.Lkp, Is.Not.Null);

            var stealth = SEntMan.EnsureComponent<CFStealthComponent>(hero);
            stealth.Active = true;
            stealth.Detection[boss] = CFDetection.Undetected;

            Goap.Think(boss);
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("HuntTarget"));
            Assert.That(Plan(boss), Is.EqualTo(new[] { "GoToLkp", "CheckNode" }));
            Assert.That(Goap.TryPickNode(boss, out var picked));
            Assert.That(picked, Is.EqualTo(node));
        });
    }

    /// <summary>Walking off to a node used to send it back to the last known position, over and over.</summary>
    [Test]
    public async Task ItStaysOnItsWayToANodeAwayFromTheLastKnownPosition()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var transform = SEntMan.System<SharedTransformSystem>();
            var boss = Boss();
            SEntMan.SpawnEntity("CFInvestigationNode", _map.Local(5, 0));
            boss.Comp.Lkp = transform.GetMapCoordinates(boss);

            Goap.Think(boss);
            Assert.That(Plan(boss), Is.EqualTo(new[] { "CheckNode" }));

            transform.SetCoordinates(boss, _map.Local(4, 0));
            Goap.Think(boss);
            Assert.That(Plan(boss), Is.EqualTo(new[] { "CheckNode" }), "It has already been there.");
        });
    }

    [Test]
    public async Task WithNothingElseToGoOnItLooksAtTheLastKnownPosition()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            boss.Comp.Lkp = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(_map.Local(5, 0));

            Goap.Think(boss);
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("Investigate"));
            Assert.That(Plan(boss), Is.EqualTo(new[] { "LookAround" }));
        });
    }

    [Test]
    public async Task PainSaysWhereAnUnseenAttackerIs()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            var hero = Hero(5);
            var stealth = SEntMan.EnsureComponent<CFStealthComponent>(hero);
            stealth.Active = true;
            stealth.Detection[boss] = CFDetection.Undetected;

            var hit = new CFDamagedEvent(hero, 1);
            SEntMan.EventBus.RaiseLocalEvent(boss, ref hit);

            Assert.That(boss.Comp.Target, Is.EqualTo(hero));
            Assert.That(boss.Comp.Lkp, Is.Not.Null);

            Goap.Think(boss);
            Assert.That(CFGOAPSystem.Knows(boss.Comp, CFGOAPFact.TargetVisible), Is.False, "It knows where from, but it cannot see them.");
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("Investigate"));
        });
    }

    [Test]
    public async Task ACorneredNpcFightsBack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            Hero(2);
            SetHealth(boss, 10);

            Goap.Think(boss);
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("StayAlive"));
            Assert.That(Plan(boss), Is.EqualTo(new[] { "Flee" }));

            var now = Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime;
            boss.Comp.CorneredUntil = now + TimeSpan.FromSeconds(5);

            Goap.Think(boss);
            Assert.That(Plan(boss), Is.EqualTo(new[] { "Close", "FightBack" }));
        });
    }

    [Test]
    public async Task ItGoesForTheOneItCanDropOverTheNearerOne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            Hero(2);
            var hurt = Hero(5);
            SetHealth(hurt, 1);

            Goap.Think(boss);
            Assert.That(boss.Comp.Target, Is.EqualTo(hurt));
        });
    }

    /// <summary>A third swing at this AC needs a natural 20, and swinging stops the pool refilling.</summary>
    [TestCase("Default", false)]
    [TestCase("Beast", true)]
    public async Task ItWeighsAThirdSwing(string tactics, bool swings)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            boss.Comp.Tactics = tactics;
            SEntMan.GetComponent<CFCombatantComponent>(boss).PrintedAttack = 8;
            SEntMan.EnsureComponent<CFActionPoolComponent>(boss).MapAttacks = 2;

            var hero = Hero(1);
            SEntMan.GetComponent<CFCombatantComponent>(hero).ArmorClass = 18;

            Goap.Think(boss);
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("KillTarget"));
            Assert.That(Plan(boss).Contains("Strike"), Is.EqualTo(swings));
        });
    }

    [Test]
    public async Task EveryKindOfStepHasASystem()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            foreach (var step in Enum.GetValues<CFGOAPStep>())
            {
                Assert.That(Goap.HasStep(step), $"Nothing runs {step}.");
            }
        });
    }

    [Test]
    public async Task ABraveNpcPrefersTheTrail()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var influence = SEntMan.System<CFGOAPInfluenceMapSystem>();
            var boss = Boss(profile: "Brave");
            var hero = Hero(5);
            SEntMan.SpawnEntity("CFInvestigationNode", _map.Local(6, 0));

            Goap.Think(boss);
            var stealth = SEntMan.EnsureComponent<CFStealthComponent>(hero);
            stealth.Active = true;
            stealth.Detection[boss] = CFDetection.Undetected;

            var grid = _map.Grid;
            var tile = SEntMan.System<Robust.Shared.GameObjects.SharedMapSystem>().CoordinatesToTile(
                grid,
                SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(grid),
                _map.Local(1, 0));
            influence.Paint(grid, CFGOAPInfluenceLayer.Scent, tile, 1f, System.Numerics.Vector2.UnitX, hero);

            Goap.Think(boss);
            Assert.That(Plan(boss), Is.EqualTo(new[] { "FollowScent" }));
        });
    }

    [Test]
    public async Task PackMatesTakeDifferentNodes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var first = Boss();
            var second = Boss(1);
            var node = SEntMan.SpawnEntity("CFInvestigationNode", _map.Local(3, 0));
            var spot = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(node);
            first.Comp.Lkp = spot;
            second.Comp.Lkp = spot;

            Assert.That(Goap.TryPickNode(second, out _));
            first.Comp.Node = node;
            Assert.That(Goap.TryPickNode(second, out _), Is.False, "Its pack mate already has that one.");
        });
    }

    [Test]
    public async Task AStealthyNpcPrefersShadowNodes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss(profile: "Stealthy");
            var lit = SEntMan.SpawnEntity("CFInvestigationNode", _map.Local(3, 0));
            var shadow = SEntMan.SpawnEntity("CFInvestigationNode", _map.Local(3, 1));
            SEntMan.GetComponent<CFGOAPInvestigationNodeComponent>(shadow).Tags = CFGOAPNodeTag.Shadow;
            boss.Comp.Lkp = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(boss);

            var picks = new Dictionary<EntityUid, int> { [lit] = 0, [shadow] = 0 };
            for (var i = 0; i < 200; i++)
            {
                Assert.That(Goap.TryPickNode(boss, out var picked));
                picks[picked]++;
            }

            Assert.That(picks[shadow], Is.GreaterThan(picks[lit]));
            Assert.That(picks[lit], Is.GreaterThan(0), "It is weighted, so it stays unpredictable.");
        });
    }

    [Test]
    public async Task ACowardKeepsOutOfTheLastKnownPosition()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss(profile: "Cowardly");
            SEntMan.SpawnEntity("CFInvestigationNode", _map.Local(4, 0));
            boss.Comp.Lkp = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(boss);

            Assert.That(Goap.TryPickNode(boss, out _), Is.False, "That node is inside its keep-out radius.");
        });
    }

    [Test]
    public async Task ACowardFleesFromANoise()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss(profile: "Cowardly");
            var noisy = Hero(0);
            SEntMan.System<CFGOAPInfluenceMapSystem>().MakeNoise(noisy);
            SEntMan.DeleteEntity(noisy);

            Goap.Think(boss);
            Assert.That(boss.Comp.Goal?.Id, Is.EqualTo("StayAlive"));
        });
    }

    /// <summary>The target hides. The NPC goes to where it last saw them, checks the node there, and Seeks.</summary>
    [TestCase("Default")]
    [TestCase("Brave")]
    [TestCase("Stealthy")]
    public async Task ItFindsAHiddenTarget(string profile)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid boss = default;
        EntityUid hero = default;
        var config = Server.ResolveDependency<IConfigurationManager>();

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            boss = SEntMan.SpawnEntity("CFGOAPTestBoss", _map.Local(0, 0));
            var goap = SEntMan.GetComponent<CFGOAPComponent>(boss);
            goap.Profile = profile;
            Give(boss, "CFLongsword");
            SEntMan.GetComponent<CFCombatantComponent>(boss).PrintedPerception = 40;

            hero = Hero(4);
            SEntMan.SpawnEntity("CFInvestigationNode", _map.Local(4, 0));
            var stealth = SEntMan.EnsureComponent<CFStealthComponent>(hero);
            stealth.Active = true;
            stealth.Detection[boss] = CFDetection.Undetected;

            goap.Target = hero;
            goap.Lkp = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(boss);
            SEntMan.System<NPCSystem>().WakeNPC(boss);
        });

        await Pair.RunTicksSync(450);

        await Server.WaitAssertion(() =>
        {
            var stealth = SEntMan.System<CFStealthSystem>();
            Assert.That(stealth.Detection(hero, boss), Is.Not.EqualTo(CFDetection.Undetected), "It searched and found them.");
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    [Test]
    public async Task AGroupThatWakesTogetherThinksApart()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var now = Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime;
            var times = new HashSet<TimeSpan>();
            for (var i = 0; i < 30; i++)
            {
                var next = Boss(i % 8, armed: false).Comp.NextThink;
                Assert.That(next, Is.InRange(now, now + TimeSpan.FromSeconds(0.5)));
                times.Add(next);
            }

            Assert.That(times, Has.Count.GreaterThan(20), "They should not all land on the same moment.");
        });
    }

    [Test]
    public async Task ArrowsLeadAroundLava()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            var maps = SEntMan.System<SharedMapSystem>();
            var grid = SEntMan.GetComponent<MapGridComponent>(_map.Grid);
            var lava = maps.CoordinatesToTile(_map.Grid, grid, _map.Local(3, 0));
            SEntMan.SpawnAtPosition("FloorLavaEntity", _map.Local(3, 0));

            var field = new CFGOAPFlowField();
            var goal = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(_map.Local(6, 0)).Position;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            SEntMan.System<CFGOAPSteeringSystem>().BuildField(boss, field, goal);
            timer.Stop();
            TestContext.Out.WriteLine($"A flow field took {timer.Elapsed.TotalMilliseconds:F1} ms for {field.Arrows.Count} tiles.");

            Assert.That(field.Arrows, Is.Not.Empty);
            Assert.That(field.Arrows.ContainsKey(lava), Is.False, "No arrow stands on lava.");

            var start = maps.CoordinatesToTile(_map.Grid, grid, _map.Local(0, 0));
            Assert.That(field.Arrows.ContainsKey(start), "It can be reached from where the boss is.");
        });
    }

    [Test]
    public async Task ANpcGoesRoundAWallToReachItsTarget()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid boss = default;
        EntityUid hero = default;
        var config = Server.ResolveDependency<IConfigurationManager>();

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            boss = SEntMan.SpawnEntity("CFGOAPTestBoss", _map.Local(0, 0));
            Give(boss, "CFLongsword");
            SEntMan.GetComponent<CFAbilityScoreComponent>(boss).AbilityModifiers[Content.Shared.CF.Preferences.CFAbility.Strength] = 30;
            SEntMan.SpawnAtPosition("WallSolid", _map.Local(3, 0));
            SEntMan.SpawnAtPosition("WallSolid", _map.Local(3, -1));
            hero = Hero(6);

            // It knows where they are, but the wall hides the line, so only the arrows get it there.
            var goap = SEntMan.GetComponent<CFGOAPComponent>(boss);
            goap.Lkp = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(boss);
            SEntMan.System<NPCSystem>().WakeNPC(boss);
        });

        // Usually about 6 seconds, and it has taken 29. Ticks are cheap, so the window is not what should fail this.
        await Pair.RunTicksSync(1200);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.LessThan(1000), "It found a way round and hit them.");
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    [Test]
    public async Task TheReachSensorFindsAnEnemyBesideIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid boss = default;
        EntityUid hero = default;

        await Server.WaitPost(() =>
        {
            boss = SEntMan.SpawnEntity("CFGOAPTestBoss", _map.Local(2, 0));
            hero = Hero(3);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(boss);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(hero);
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<CFFlankingSystem>().EnemiesInReach(boss), Does.Contain(hero));
        });
    }

    [Test]
    public async Task ThinkingStaysCheapWithAFullEncounter()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var bosses = new List<Entity<CFGOAPComponent>>();
            for (var i = 0; i < 30; i++)
            {
                bosses.Add(Boss(i % 8, armed: false));
            }

            for (var i = 0; i < 10; i++)
            {
                SEntMan.AddComponent<CFGOAPScentEmitterComponent>(Hero(i % 8));
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            foreach (var boss in bosses)
            {
                Goap.Think(boss);
            }

            timer.Stop();
            TestContext.Out.WriteLine($"30 NPCs thinking once took {timer.Elapsed.TotalMilliseconds:F1} ms cold.");

            timer.Restart();
            foreach (var boss in bosses)
            {
                Goap.Think(boss);
            }

            timer.Stop();
            TestContext.Out.WriteLine($"30 NPCs thinking once took {timer.Elapsed.TotalMilliseconds:F1} ms warm.");
            Assert.That(timer.Elapsed.TotalSeconds, Is.LessThan(1), "They think twice a second, so this has to be far below that.");
        });
    }

    [Test]
    public async Task ARealNpcHuntsDownAndHitsItsTarget()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid boss = default;
        EntityUid hero = default;
        var config = Server.ResolveDependency<IConfigurationManager>();

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            boss = SEntMan.SpawnEntity("CFGOAPTestBoss", _map.Local(0, 0));
            Give(boss, "CFLongsword");
            SEntMan.GetComponent<CFAbilityScoreComponent>(boss).AbilityModifiers[Content.Shared.CF.Preferences.CFAbility.Strength] = 30;
            hero = Hero(4);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(boss);
            SEntMan.System<NPCSystem>().WakeNPC(boss);
        });

        await Pair.RunTicksSync(300);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CFGOAPComponent>(boss).Target, Is.EqualTo(hero));
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.LessThan(1000), "It walked over and hit them.");
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }
}
