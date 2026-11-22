using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;
using Content.Server.Atmos.EntitySystems;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.DecisionLog;
using Content.Server.CF.GOAP.Steering;
using Content.Server.NPC.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.CCVar;
using Content.Shared.CF.GOAP;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>
/// Long walks on <c>npc_test_map</c>, a maze of one-tile corridors, which the arena has none of. These load their own
/// map, so they are the exception to using <see cref="CFDevMap"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(CFGOAPSteeringSystem))]
public sealed class CFGOAPMazeTest : CFGOAPLoggedTest
{
    private static readonly ResPath MapPath = new("/Maps/Test/npc_test_map.yml");

    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: [ MobHuman, CFGOAPBrain ]
  id: CFMazeTestBoss
  components:
  - type: CFCombatant
    level: 1
    armorClass: 15
    maxHp: 60
    currentHp: 60
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Monster
";

    private EntityUid _grid;

    private SharedTransformSystem Xform => SEntMan.System<SharedTransformSystem>();

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private IConfigurationManager Config => Server.ResolveDependency<IConfigurationManager>();

    private async Task Load(bool npcs = true)
    {
        await Server.WaitPost(() =>
        {
            var loader = SEntMan.System<MapLoaderSystem>();
            Assert.That(loader.TryLoadMap(MapPath, out _, out var grids, new DeserializationOptions { InitializeMaps = true }));
            _grid = grids!.First().Owner;
            Config.SetCVar(CCVars.NPCEnabled, npcs);
        });
    }

    private EntityCoordinates At(int x, int y)
    {
        return new EntityCoordinates(_grid, x + 0.5f, y + 0.5f);
    }

    /// <summary>A creature standing on one tile that calls another one home.</summary>
    private Entity<CFGOAPComponent> Boss((int X, int Y) tile, (int X, int Y) home)
    {
        var boss = SEntMan.SpawnEntity("CFMazeTestBoss", At(tile.X, tile.Y));
        var goap = SEntMan.GetComponent<CFGOAPComponent>(boss);
        goap.Home = At(home.X, home.Y);
        SEntMan.System<SharedPhysicsSystem>().WakeBody(boss);
        SEntMan.System<NPCSystem>().WakeNPC(boss);
        return (boss, goap);
    }

    private float DistanceHome(Entity<CFGOAPComponent> boss)
    {
        var home = Xform.ToMapCoordinates(boss.Comp.Home!.Value).Position;
        return (home - Xform.GetMapCoordinates(boss).Position).Length();
    }

    /// <summary>Runs the game a sixth of a second at a time until <paramref name="done"/>, or the limit in seconds.</summary>
    private async Task RunUntil(float seconds, Func<bool> done)
    {
        var finished = false;
        for (var i = 0; i < seconds * 6 && !finished; i++)
        {
            await Pair.RunTicksSync(Timing.TickRate / 6);
            await Server.WaitPost(() => finished = done());
        }
    }

    private async Task NpcsOff()
    {
        await Server.WaitPost(() => Config.SetCVar(CCVars.NPCEnabled, false));
    }

    [Test]
    public async Task WithinItsRangeItIdlesAndOutsideItGoesHome()
    {
        await Load(npcs: false);
        string near = null;
        string far = null;
        await Server.WaitPost(() =>
        {
            var goap = SEntMan.System<CFGOAPSystem>();
            var atHome = Boss((-10, 7), (-10, 7));
            var away = Boss((12, 7), (-10, 7));
            goap.Think(atHome);
            goap.Think(away);
            near = atHome.Comp.Goal?.Id;
            far = away.Comp.Goal?.Id;
        });

        Assert.That(near, Is.EqualTo("Idle"), "A goal already met has nothing to plan.");
        Assert.That(far, Is.EqualTo("GoHome"));
    }

    [Test]
    public async Task TheDecisionLogKeepsWhatItChose()
    {
        await Load(npcs: false);
        var entries = new List<string>();
        await Server.WaitPost(() =>
        {
            var boss = Boss((12, 7), (-10, 7));
            SEntMan.System<CFGOAPSystem>().Think(boss);
            SEntMan.System<CFGOAPDecisionLogSystem>().WatchAll();
            entries.AddRange(SEntMan.GetComponent<CFGOAPDecisionLogComponent>(boss).Entries);
        });

        Assert.That(entries.Any(entry => entry.EndsWith("goal GoHome")), string.Join("\n", entries));
    }

    /// <summary>The one-tile corridor where a mob used to catch the corners and go back and forth.</summary>
    [Test]
    public async Task ItDoesNotTurnBackInACorridor()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        var furthest = float.MinValue;
        var backtrack = 0f;
        var x = 0f;
        await Server.WaitPost(() =>
        {
            boss = Boss((-3, 5), (-3, 5));
            boss.Comp.Lkp = Xform.ToMapCoordinates(At(12, 7));
        });

        await RunUntil(30f, () =>
        {
            x = Xform.GetMoverCoordinates(boss).Position.X;
            furthest = MathF.Max(furthest, x);
            backtrack = MathF.Max(backtrack, furthest - x);
            return x >= 6.5f;
        });
        await NpcsOff();

        Assert.That(x, Is.GreaterThanOrEqualTo(6.5f), "It never reached the end of the corridor.");
        Assert.That(backtrack, Is.LessThan(1f), "It turned back in the corridor.");
    }

    /// <summary>
    /// The door at (0, -5) leads south into a corridor that turns east, so the goal is out of a straight line from both
    /// sides of it. It opens the door, walks through, and builds its field again from the far side.
    /// </summary>
    [Test]
    public async Task ItWalksThroughADoorThenBuildsANewField()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        var passed = false;
        var buildBefore = TimeSpan.Zero;
        var rebuilt = false;
        var distance = float.MaxValue;
        var goal = At(5, -9);
        await Server.WaitPost(() =>
        {
            boss = Boss((0, -2), (5, -9));
            boss.Comp.Lkp = Xform.ToMapCoordinates(goal);
        });

        await RunUntil(30f, () =>
        {
            if (SEntMan.TryGetComponent(boss, out CFGOAPSteeringComponent steering))
            {
                if (steering.PassingThrough && !passed)
                {
                    passed = true;
                    buildBefore = steering.OwnField.NextBuild;
                }
                else if (passed && !steering.PassingThrough && steering.OwnField.NextBuild > buildBefore)
                {
                    rebuilt = true;
                }
            }

            distance = (Xform.ToMapCoordinates(goal).Position - Xform.GetMapCoordinates(boss).Position).Length();
            return rebuilt && distance <= 1.5f;
        });
        await NpcsOff();

        Assert.That(passed, "It never walked through a door it opened.");
        Assert.That(rebuilt, "It did not build its field again on the far side.");
        Assert.That(distance, Is.LessThanOrEqualTo(1.5f), "It never got there.");
    }

    [Test]
    public async Task ItWalksHomeAcrossTheMaze()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        var usedRoute = false;
        var distance = float.MaxValue;
        var start = TimeSpan.Zero;
        var took = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            boss = Boss((12, 7), (-10, 7));
            start = Timing.CurTime;
        });

        await RunUntil(120f, () =>
        {
            usedRoute |= SEntMan.TryGetComponent(boss, out CFGOAPSteeringComponent steering) && steering.Route.Count > 0;
            distance = DistanceHome(boss);
            took = Timing.CurTime - start;
            return distance <= CFGOAPSystem.HomeArrival;
        });
        await NpcsOff();

        TestContext.Out.WriteLine(FormattableString.Invariant($"Home in {took.TotalSeconds:F1} seconds."));
        Assert.That(distance, Is.LessThanOrEqualTo(CFGOAPSystem.HomeArrival), "It never got home.");
        Assert.That(usedRoute, "A field does not reach that far, so it should have needed a route.");
    }

    /// <summary>Nothing else starts it moving, and with a clear line there is no field to do it either.</summary>
    [Test]
    public async Task ItSetsOffHomeWithAClearLine()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        var distance = float.MaxValue;
        var clear = false;
        await Server.WaitPost(() =>
        {
            boss = Boss((-1, -3), (0, 0));
            boss.Comp.HomeRange = 1f;
            clear = SEntMan.System<CFGOAPSteeringSystem>().CanWalkStraight(boss, Xform.ToMapCoordinates(boss.Comp.Home!.Value).Position);
        });

        await RunUntil(10f, () =>
        {
            distance = DistanceHome(boss);
            return distance <= CFGOAPSystem.HomeArrival;
        });
        await NpcsOff();

        Assert.That(clear, "This test only means something with a clear line home.");
        Assert.That(distance, Is.LessThanOrEqualTo(CFGOAPSystem.HomeArrival), "It never set off.");
    }

    private void SetAlight(EntityUid mob, float stacks)
    {
        var flammable = SEntMan.System<FlammableSystem>();
        flammable.AdjustFireStacks(mob, stacks, ignite: true);
    }

    [Test]
    public async Task PuttingOutAFireComesBeforeEverythingElse()
    {
        await Load(npcs: false);
        string goal = null;
        var fact = false;
        await Server.WaitPost(() =>
        {
            var boss = Boss((0, 0), (0, 0));
            SetAlight(boss, 5f);
            SEntMan.System<CFGOAPSystem>().Think(boss);
            goal = boss.Comp.Goal?.Id;
            fact = CFGOAPSystem.Knows(boss.Comp, CFGOAPFact.OnFire);
        });

        Assert.That(fact);
        Assert.That(goal, Is.EqualTo("PutOutFire"));
    }

    /// <summary>It would only catch fire again, so it has to get off the lava first.</summary>
    [Test]
    public async Task ItDoesNotRollOnLava()
    {
        await Load(npcs: false);
        string goal = null;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity("FloorLavaEntity", At(0, 0));
            var boss = Boss((0, 0), (0, 0));
            SetAlight(boss, 5f);
            SEntMan.System<CFGOAPSystem>().Think(boss);
            goal = boss.Comp.Goal?.Id;
        });

        Assert.That(goal, Is.Not.EqualTo("PutOutFire"));
    }

    [Test]
    public async Task ItDropsAndRollsUntilTheFireIsOut()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        var burning = true;
        var dropped = false;
        var start = TimeSpan.Zero;
        var took = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            boss = Boss((0, 0), (0, 0));
            var sword = SEntMan.SpawnEntity("CFLongsword", At(0, 0));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(boss, sword, checkActionBlocker: false));
            SetAlight(boss, 5f);
            start = Timing.CurTime;
        });

        await RunUntil(40f, () =>
        {
            burning = SEntMan.GetComponent<FlammableComponent>(boss).OnFire;
            dropped |= !SEntMan.System<SharedHandsSystem>().EnumerateHeld(boss.Owner).Any();
            took = Timing.CurTime - start;
            return !burning;
        });
        await NpcsOff();

        TestContext.Out.WriteLine(FormattableString.Invariant($"Out in {took.TotalSeconds:F1} seconds."));
        Assert.That(burning, Is.False, "It never put itself out.");
        Assert.That(dropped, "Rolling should make it drop what it holds, which shows it really rolled.");
        Assert.That(took.TotalSeconds, Is.LessThan(25), "Left alone, 5 stacks take 50 seconds to fade, so it did not roll.");
    }

    /// <summary>The home tile is inside the corner of the outer wall, so nothing can reach it.</summary>
    [TestCase(12, 7)]
    [TestCase(-2, 5)]
    public async Task ASealedHomeGivesNoWayThenIdle(int x, int y)
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        var noWay = false;
        string goal = null;
        await Server.WaitPost(() => boss = Boss((x, y), (-11, 9)));

        await RunUntil(30f, () =>
        {
            noWay = CFGOAPSystem.Knows(boss.Comp, CFGOAPFact.NoWay);
            goal = boss.Comp.Goal?.Id;
            return noWay && goal == "Idle";
        });
        await NpcsOff();

        Assert.That(noWay, "It never decided there was no way home.");
        Assert.That(goal, Is.EqualTo("Idle"));
    }

    /// <summary>Past the edge of its range it should walk all the way home, not flick between wandering and going home.</summary>
    [Test]
    public async Task ItDoesNotDitherAtTheEdgeOfItsRange()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        string goal = null;
        var trips = 0;
        var furthestAtEndOfTrip = 0f;
        var furthest = 0f;
        await Server.WaitPost(() =>
        {
            boss = Boss((0, 0), (0, 0));
            boss.Comp.HomeRange = 3f;
        });

        await RunUntil(90f, () =>
        {
            var now = boss.Comp.Goal?.Id;
            var distance = DistanceHome(boss);
            furthest = MathF.Max(furthest, distance);
            if (now != goal && goal == "GoHome")
            {
                trips++;
                furthestAtEndOfTrip = MathF.Max(furthestAtEndOfTrip, distance);
            }

            goal = now;
            return false;
        });
        await NpcsOff();

        TestContext.Out.WriteLine(FormattableString.Invariant($"{trips} trips home. Furthest {furthest:F1} tiles."));
        // Idle walks only pick spots inside the range, so it should never need sending home at all. If it does, the
        // trip must still end at home, not at the edge where it would flick in and out.
        Assert.That(furthest, Is.GreaterThan(1f), "It walks about.");
        Assert.That(furthest, Is.LessThanOrEqualTo(boss.Comp.HomeRange + 0.5f), "Idle walks keep inside its range.");
        Assert.That(furthestAtEndOfTrip, Is.LessThanOrEqualTo(CFGOAPSystem.HomeArrival + 1f), "A trip ended short of home, so it is flicking at the edge of its range.");
    }
}
