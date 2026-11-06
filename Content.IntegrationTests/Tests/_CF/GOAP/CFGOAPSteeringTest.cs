using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP.Steering;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.CCVar;
using Content.Shared.CF.Detection;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>Steering behaviours on a real NPC.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPSteeringSystem))]
public sealed class CFGOAPSteeringTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSteeringMob
  components:
  - type: HTN
    rootTask:
      task: IdleCompound

- type: entity
  parent: CFSteeringMob
  id: CFSteeringAlly
  components:
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFSteeringMob
  id: CFSteeringEnemy
  components:
  - type: CFFaction
    faction: Monster
";

    [Test]
    public async Task AFleeingNpcStopsShortOfLava()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid mob = default;
        var transform = SEntMan.System<SharedTransformSystem>();
        var config = Server.ResolveDependency<IConfigurationManager>();

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            mob = SEntMan.SpawnEntity("CFSteeringMob", map.Local(3, 0));
            var threat = SEntMan.SpawnEntity("CFSteeringMob", map.Local(2, 0));
            for (var y = -1; y <= 1; y++)
            {
                SEntMan.SpawnAtPosition("FloorLavaEntity", map.Local(6, y));
            }

            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
            SEntMan.System<NPCSystem>().WakeNPC(mob);
            SEntMan.System<CFGOAPSteeringSystem>().Steer(
                mob,
                new List<CFGOAPSteeringBehavior> { new() { Kind = CFGOAPSteeringKind.Flee } },
                threat);
        });

        await Pair.RunTicksSync(120);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<FlammableComponent>(mob).OnFire, Is.False, "It should not have touched the lava.");
            Assert.That(SEntMan.System<CFGOAPSteeringSystem>().IsDangerous(transform.GetMapCoordinates(mob)), Is.False);
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    /// <summary>Straight at a wall, nothing has any interest left, which is the case that could freeze it.</summary>
    [Test]
    public async Task ANpcSlidesAroundAWallItRunsInto()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid mob = default;
        var transform = SEntMan.System<SharedTransformSystem>();
        var config = Server.ResolveDependency<IConfigurationManager>();
        var goal = Vector2.Zero;

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            mob = SEntMan.SpawnEntity("CFSteeringMob", map.Local(0, 0));
            SEntMan.SpawnAtPosition("WallSolid", map.Local(3, 0));

            goal = transform.ToMapCoordinates(map.Local(6, 0)).Position;
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
            SEntMan.System<NPCSystem>().WakeNPC(mob);
            SEntMan.System<CFGOAPSteeringSystem>().Steer(
                mob,
                new List<CFGOAPSteeringBehavior> { new() { Kind = CFGOAPSteeringKind.Arrive } },
                point: goal);
        });

        await Pair.RunTicksSync(180);

        await Server.WaitAssertion(() =>
        {
            var steering = SEntMan.GetComponent<NPCSteeringComponent>(mob);
            Assert.That(float.IsNaN(steering.Interest[0]), Is.False, "A NaN here would stop it for good, with no error.");
            Assert.That(Vector2.Distance(transform.GetWorldPosition(mob), goal), Is.LessThan(1.5f), "It gets past the wall to the goal.");
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    /// <summary>A mob sliding past lava misses every ray, because they point ahead of it.</summary>
    [Test]
    public async Task TheSafetyCircleSnapsAwayFromLavaBeside()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid mob = default;
        var config = Server.ResolveDependency<IConfigurationManager>();
        var transform = SEntMan.System<SharedTransformSystem>();

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);

            // Heading along a row of lava, close enough for its body to clip it.
            mob = SEntMan.SpawnEntity("CFSteeringMob", map.Local(0, 0).Offset(new Vector2(0f, 0.1f)));
            for (var x = -1; x <= 6; x++)
            {
                SEntMan.SpawnAtPosition("FloorLavaEntity", map.Local(x, 1));
            }

            var goal = transform.ToMapCoordinates(map.Local(6, 0)).Position + new Vector2(0f, 0.1f);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
            SEntMan.System<NPCSystem>().WakeNPC(mob);
            SEntMan.System<CFGOAPSteeringSystem>().Steer(
                mob,
                new List<CFGOAPSteeringBehavior> { new() { Kind = CFGOAPSteeringKind.Arrive } },
                point: goal);
        });

        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<FlammableComponent>(mob).OnFire, Is.False);
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    [Test]
    public async Task RaysTouchWallsLavaAndEnemies()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var steering = SEntMan.System<CFGOAPSteeringSystem>();
            var transform = SEntMan.System<SharedTransformSystem>();
            var mob = SEntMan.SpawnEntity("CFSteeringAlly", map.Local(0, 0));
            var from = transform.GetWorldPosition(mob);
            var ally = SEntMan.SpawnEntity("CFSteeringAlly", map.Local(1, 0));
            var east = Vector2.Normalize(transform.GetWorldPosition(ally) - from);

            Assert.That(steering.TryFeel(mob, from, east, 6f, out _), Is.False, "An ally does not stop a ray.");

            var enemy = SEntMan.SpawnEntity("CFSteeringEnemy", map.Local(5, 0));
            Assert.That(steering.TryFeel(mob, from, east, 6f, out var seen));
            Assert.That(seen.Entity, Is.EqualTo(enemy), "A ray reaches an enemy past an ally.");

            var stealth = SEntMan.EnsureComponent<CFStealthComponent>(enemy);
            stealth.Active = true;
            stealth.Detection[mob] = CFDetection.Undetected;
            Assert.That(steering.TryFeel(mob, from, east, 6f, out _), Is.False, "It may not know what it cannot perceive.");
            stealth.Active = false;

            var lava = SEntMan.SpawnAtPosition("FloorLavaEntity", map.Local(3, 0));
            Assert.That(steering.TryFeel(mob, from, east, 6f, out var toLava), "Rays must reach lava even though it is not hard.");
            Assert.That(toLava.Entity, Is.EqualTo(lava));
            Assert.That(toLava.Distance, Is.EqualTo(2.5f).Within(0.1f), "It touches the near edge.");

            SEntMan.DeleteEntity(lava);
            var wall = SEntMan.SpawnAtPosition("WallSolid", map.Local(3, 0));
            Assert.That(steering.TryFeel(mob, from, east, 6f, out var toWall));
            Assert.That(toWall.Entity, Is.EqualTo(wall), "A wall hides what is behind it.");
            Assert.That(steering.TryFeel(mob, from, east, 1f, out _), Is.False, "It only reaches as far as its length.");
        });
    }

    [Test]
    public async Task AFleeingNpcRunsAndStaysOnFloor()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid mob = default;
        EntityUid threat = default;
        var transform = SEntMan.System<SharedTransformSystem>();
        var config = Server.ResolveDependency<IConfigurationManager>();

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            mob = SEntMan.SpawnEntity("CFSteeringMob", map.Local(3, 0));
            threat = SEntMan.SpawnEntity("CFSteeringMob", map.Local(2, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
            SEntMan.System<NPCSystem>().WakeNPC(mob);
            SEntMan.System<CFGOAPSteeringSystem>().Steer(
                mob,
                new List<CFGOAPSteeringBehavior> { new() { Kind = CFGOAPSteeringKind.Flee } },
                threat);
        });

        var start = 0f;
        await Server.WaitPost(() =>
            start = Vector2.Distance(transform.GetWorldPosition(mob), transform.GetWorldPosition(threat)));

        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            var position = transform.GetWorldPosition(mob);
            Assert.That(Vector2.Distance(position, transform.GetWorldPosition(threat)), Is.GreaterThan(start + 1f));

            var grid = map.Grid;
            var maps = SEntMan.System<SharedMapSystem>();
            var tile = maps.CoordinatesToTile(grid, SEntMan.GetComponent<MapGridComponent>(grid), transform.GetMoverCoordinates(mob));
            Assert.That(SEntMan.System<TurfSystem>().IsTileBlocked(grid, tile, Content.Shared.Physics.CollisionGroup.Impassable), Is.False, "It never ends inside a wall.");
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }
}
