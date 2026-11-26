using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP.Steering;
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
            SEntMan.System<CFGOAPSteeringSystem>().Steer(
                mob,
                new List<CFGOAPSteeringBehavior> { new() { Kind = CFGOAPSteeringKind.Arrive } },
                point: goal);
        });

        await Pair.RunTicksSync(180);

        await Server.WaitAssertion(() =>
        {
            var steering = SEntMan.GetComponent<CFGOAPSteeringComponent>(mob);
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

    /// <summary>
    /// Walks the mob toward <paramref name="goal"/> one tick at a time, calling <c>Navigate</c> each tick as a step
    /// would, until it arrives or <paramref name="seconds"/> run out. Reports how often it reversed sideways (the
    /// rattle between two walls), how often it changed between arrows and a straight line, and how far off the row
    /// it strayed.
    /// </summary>
    private async Task<(bool Arrived, int Reversals, int Switches, float WorstOffset)> Walk(CFDevMap map, EntityUid mob, Vector2 goal, float seconds, bool trace = false)
    {
        var transform = SEntMan.System<SharedTransformSystem>();
        var steering = SEntMan.System<CFGOAPSteeringSystem>();
        var centreY = transform.ToMapCoordinates(map.Local(0, 0)).Position.Y;
        var arrived = false;
        var reversals = 0;
        var switches = 0;
        var worst = 0f;
        var lastSide = 0;
        var lastField = false;
        var wasBurning = false;
        for (var tick = 0; tick < seconds * 30 && !arrived; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                steering.Navigate(mob, goal, null);
                var position = transform.GetWorldPosition(mob);
                var velocity = SEntMan.GetComponent<PhysicsComponent>(mob).LinearVelocity;
                var side = velocity.Y > 0.3f ? 1 : velocity.Y < -0.3f ? -1 : 0;
                if (side != 0 && lastSide != 0 && side != lastSide)
                    reversals++;

                if (side != 0)
                    lastSide = side;

                worst = MathF.Max(worst, MathF.Abs(position.Y - centreY));
                var comp = SEntMan.GetComponent<CFGOAPSteeringComponent>(mob);
                if (comp.UsingField != lastField)
                {
                    switches++;
                    lastField = comp.UsingField;
                }

                arrived = Vector2.Distance(position, goal) < 1f;
                var burning = SEntMan.TryGetComponent(mob, out FlammableComponent flammable) && flammable.OnFire;
                if (trace && burning && !wasBurning)
                    TestContext.Out.WriteLine(FormattableString.Invariant($"FIRE t{tick}: at ({position.X - goal.X + 6f:F2}, {position.Y - centreY:F2}) field {comp.UsingField} arrows {comp.OnArrows}"));

                wasBurning = burning;
                if (trace && tick < 24)
                {
                    int bi = 0, bd = 0;
                    for (var i = 1; i < comp.Interest.Length; i++)
                    {
                        if (comp.Interest[i] > comp.Interest[bi]) bi = i;
                        if (comp.Danger[i] > comp.Danger[bd]) bd = i;
                    }

                    var hazards = 0;
                    foreach (var fixedThing in comp.PanicFixed)
                    {
                        if (fixedThing.Dangerous) hazards++;
                    }

                    TestContext.Out.WriteLine(FormattableString.Invariant($"d{tick}: at ({position.X - goal.X + 6f:F2}, {position.Y - centreY:F2}) v ({velocity.X:F2}, {velocity.Y:F2}) sent ({comp.LastSteerDirection.X:F2}, {comp.LastSteerDirection.Y:F2}) interest slot {bi}={comp.Interest[bi]:F2} danger slot {bd}={comp.Danger[bd]:F2} east i={comp.Interest[0]:F2} d={comp.Danger[0]:F2} avoid ({comp.AvoidDirection.X:F2}, {comp.AvoidDirection.Y:F2}) nearDanger {comp.NearDanger} hazards {hazards} arrows {comp.OnArrows}"));
                }

                if (trace && tick % 6 == 0)
                {
                    var kind = comp.Behaviors.Count > 0 ? comp.Behaviors[0].Kind.ToString() : "none";
                    TestContext.Out.WriteLine(FormattableString.Invariant($"t{tick}: at ({position.X - goal.X + 6f:F2}, {position.Y - centreY:F2}) v ({velocity.X:F2}, {velocity.Y:F2}) field {comp.UsingField} arrows {comp.OnArrows} {kind} settled {comp.Settled} moving {comp.Moving} point {comp.Point}"));
                }
            });
        }

        TestContext.Out.WriteLine(FormattableString.Invariant($"arrived {arrived}, reversals {reversals}, field switches {switches}, worst offset {worst:F2}"));
        return (arrived, reversals, switches, worst);
    }

    /// <summary>A one-tile corridor: the walls on both sides must not throw it from side to side.</summary>
    [Test]
    public async Task ItWalksAOneTileCorridorWithoutRattling()
    {
        var map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var config = Server.ResolveDependency<IConfigurationManager>();
        var transform = SEntMan.System<SharedTransformSystem>();
        EntityUid mob = default;
        var goal = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            mob = SEntMan.SpawnEntity("CFSteeringMob", map.Local(0, 0).Offset(new Vector2(0f, 0.1f)));
            for (var x = 0; x <= 5; x++)
            {
                SEntMan.SpawnAtPosition("WallSolid", map.Local(x, -1));
                SEntMan.SpawnAtPosition("WallSolid", map.Local(x, 1));
            }

            goal = transform.ToMapCoordinates(map.Local(6, 0)).Position;
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
            SEntMan.EnsureComponent<CFGOAPSteeringComponent>(mob);
        });

        var result = await Walk(map, mob, goal, 10f);
        await Server.WaitPost(() => config.SetCVar(CCVars.NPCEnabled, false));

        Assert.That(result.Arrived, "It gets through the corridor.");
        Assert.That(result.Reversals, Is.LessThanOrEqualTo(2), "It rattled between the walls.");
    }

    /// <summary>
    /// A one-tile bridge with lava either side. Both sides push on the safety circle at once, and the pushes have to
    /// correct it gently, not throw it across to the other edge and back.
    /// </summary>
    [Test]
    public async Task ItCrossesAOneTileBridgeWithoutRattling()
    {
        var map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var config = Server.ResolveDependency<IConfigurationManager>();
        var transform = SEntMan.System<SharedTransformSystem>();
        EntityUid mob = default;
        var goal = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            mob = SEntMan.SpawnEntity("CFSteeringMob", map.Local(0, 0).Offset(new Vector2(0f, 0.1f)));
            for (var x = 0; x <= 5; x++)
            {
                SEntMan.SpawnAtPosition("FloorLavaEntity", map.Local(x, -1));
                SEntMan.SpawnAtPosition("FloorLavaEntity", map.Local(x, 1));
            }

            goal = transform.ToMapCoordinates(map.Local(6, 0)).Position;
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
            SEntMan.EnsureComponent<CFGOAPSteeringComponent>(mob);
        });

        var result = await Walk(map, mob, goal, 10f);
        await Server.WaitPost(() => config.SetCVar(CCVars.NPCEnabled, false));

        Assert.That(SEntMan.GetComponent<FlammableComponent>(mob).OnFire, Is.False, "It touched the lava.");
        Assert.That(result.Arrived, "It gets across the bridge.");
        Assert.That(result.Reversals, Is.LessThanOrEqualTo(2), "It rattled between the two edges.");
    }

    /// <summary>
    /// A chasm made of empty tiles stops no ray, so the straight-line check has to look at the floor as well: clear
    /// along a one-tile bridge, and not clear across the gap beside it.
    /// </summary>
    [Test]
    public async Task AStraightLineNeedsFloorUnderIt()
    {
        var map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var along = false;
        var across = true;
        await Server.WaitPost(() =>
        {
            var transform = SEntMan.System<SharedTransformSystem>();
            var maps = SEntMan.System<SharedMapSystem>();
            var steering = SEntMan.System<CFGOAPSteeringSystem>();
            var grid = SEntMan.GetComponent<MapGridComponent>(map.Grid);
            var mob = SEntMan.SpawnEntity("CFSteeringMob", map.Local(0, 0));

            // Holes at x 2 and 3 on the row above. The row itself is left as the bridge.
            for (var x = 2; x <= 3; x++)
            {
                maps.SetTile(map.Grid, grid, maps.LocalToTile(map.Grid, grid, map.Local(x, 1)), Tile.Empty);
            }

            along = steering.CanWalkStraight(mob, transform.ToMapCoordinates(map.Local(5, 0)).Position);
            transform.SetCoordinates(mob, map.Local(1, 1));
            across = steering.CanWalkStraight(mob, transform.ToMapCoordinates(map.Local(4, 1)).Position);
        });

        Assert.That(along, "The bridge beside the holes is a straight walk.");
        Assert.That(across, Is.False, "A line over the holes is not, though no ray is stopped.");
    }
}
