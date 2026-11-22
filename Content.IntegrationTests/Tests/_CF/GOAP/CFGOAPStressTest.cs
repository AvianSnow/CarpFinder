using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Content.IntegrationTests.Fixtures;
using Content.Server.CF.GOAP;
using Content.Shared.CCVar;
using Content.Shared.CF;
using Content.Shared.CF.Combat;
using Content.Shared.CF.GOAP;
using Content.Shared.NPC;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>
/// Measures what GOAP costs against stock HTN, with the same kobolds on the same map. Not a pass or fail test: it
/// prints a table. It only runs with <c>CF_GOAP_STRESS=1</c> set, because it takes minutes:
/// <code>
/// CF_GOAP_STRESS=1 dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Tools --no-build \
///   --filter "FullyQualifiedName~CFGOAPStressTest" --logger "console;verbosity=detailed"
/// </code>
/// It loads <c>npc_test_map</c>, a maze with doors, windows and tables, because the arena has no room for 100 mobs.
/// Each run spawns the mobs from one seed, so every mode gets the same places. Per system times come from the
/// engine's own update timing (<see cref="IEntitySystemManager.MetricsEnabled"/>).
/// </summary>
[TestFixture]
public sealed class CFGOAPStressTest : GameTest
{
    private static readonly ResPath MapPath = new("/Maps/Test/npc_test_map.yml");

    private const int WarmupTicks = 150;
    private const int MeasuredTicks = 900;
    private const int Heroes = 5;
    private const int Seed = 1234;

    /// <summary>A real server runs 30 ticks a second, so this is all the time one tick has.</summary>
    private const double TickBudgetMs = 1000.0 / 30;

    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: CFKoboldWarrior
  id: CFStressTestKobold

- type: entity
  parent: MobHuman
  id: CFStressTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 12
    maxHp: 1000000
    currentHp: 1000000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha
";

    public enum Mode
    {
        /// <summary>The mobs stand there with every NPC switched off. What the AI costs is the rest.</summary>
        None,
        Htn,
        Goap,

        /// <summary>GOAP with sleep on and no player anywhere, as a mob far from the party is.</summary>
        GoapAsleep,
    }

    private sealed record Result(
        Mode Mode,
        bool Fight,
        int Mobs,
        double AvgMs,
        double P95Ms,
        double MaxMs,
        double AiMs,
        int Awake,
        long HeroDamage,
        List<(string System, double Ms)> Systems);

    private IConfigurationManager Config => Server.ResolveDependency<IConfigurationManager>();

    [Test]
    public async Task CompareGoapWithStockHtn()
    {
        if (Environment.GetEnvironmentVariable("CF_GOAP_STRESS") != "1")
            Assert.Ignore("A benchmark. Set CF_GOAP_STRESS=1 to run it.");

        // Thrown away: the first runs pay for the JIT, which made 30 idle mobs look slower than 60.
        await Run(Mode.Htn, true, 30);
        await Run(Mode.Goap, true, 30);

        var results = new List<Result>();
        foreach (var count in new[] { 30, 60, 100 })
        {
            foreach (var fight in new[] { false, true })
            {
                foreach (var mode in Enum.GetValues<Mode>())
                {
                    if (mode == Mode.GoapAsleep && fight)
                        continue;

                    var result = await Run(mode, fight, count);
                    results.Add(result);
                    TestContext.Progress.WriteLine(Line(result));
                }
            }
        }

        await Server.WaitPost(() =>
        {
            Config.SetCVar(CCVars.NPCEnabled, false);
            Config.SetCVar(CFCVars.GoapSleep, false);
        });

        var report = Report(results);
        TestContext.Out.WriteLine(report);
        if (Environment.GetEnvironmentVariable("CF_GOAP_STRESS_OUT") is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, report);
    }

    private async Task<Result> Run(Mode mode, bool fight, int count)
    {
        var entities = SEntMan;
        MapId mapId = default;
        var mobs = new List<EntityUid>();
        var heroes = new List<EntityUid>();

        await Server.WaitPost(() =>
        {
            Config.SetCVar(CCVars.NPCEnabled, mode != Mode.None);
            Config.SetCVar(CFCVars.GoapSleep, mode == Mode.GoapAsleep);

            var loader = entities.System<MapLoaderSystem>();
            Assert.That(loader.TryLoadMap(MapPath, out var map, out var grids, new DeserializationOptions { InitializeMaps = true }));
            mapId = map!.Value.Comp.MapId;
            var grid = grids!.MaxBy(g => entities.System<SharedMapSystem>().GetAllTiles(g.Owner, g.Comp).Count());

            var free = FreeTiles(grid);
            var random = new System.Random(Seed + count);
            var heroTiles = new List<Vector2i>();
            if (fight)
            {
                for (var i = 0; i < Heroes; i++)
                {
                    var tile = free[random.Next(free.Count)];
                    heroTiles.Add(tile);
                    heroes.Add(entities.SpawnEntity("CFStressTestHero", Centre(grid, tile)));
                }
            }

            // Every CF monster has a brain, so the stock runs take it out again: the same body on stock HTN.
            var stock = mode is Mode.None or Mode.Htn;
            for (var i = 0; i < count; i++)
            {
                // In a fight every mob starts within 30 feet of a hero, so all of them have something to do.
                var options = fight
                    ? free.Where(t => Near(t, heroTiles[i % Heroes])).ToList()
                    : free;
                var mob = entities.SpawnEntity("CFStressTestKobold", Centre(grid, options[random.Next(options.Count)]));
                if (stock)
                    CFStockAi.MakeStock(entities, mob);

                mobs.Add(mob);
                if (mode == Mode.GoapAsleep)
                    entities.GetComponent<CFGOAPComponent>(mob).SleepDelay = TimeSpan.Zero;
            }
        });

        await Server.WaitRunTicks(WarmupTicks);

        var systems = new List<Type>();
        await Server.WaitPost(() =>
        {
            systems = entities.EntitySysManager.GetEntitySystemTypes().ToList();
            entities.EntitySysManager.MetricsEnabled = true;
        });
        var before = Snapshot(systems);

        var ticks = new List<double>(MeasuredTicks);
        var watch = new Stopwatch();
        for (var i = 0; i < MeasuredTicks; i++)
        {
            watch.Restart();
            await Server.WaitRunTicks(1);
            ticks.Add(watch.Elapsed.TotalMilliseconds);
        }

        await Server.WaitPost(() => entities.EntitySysManager.MetricsEnabled = false);
        var after = Snapshot(systems);

        var awake = 0;
        long damage = 0;
        await Server.WaitPost(() =>
        {
            awake = mobs.Count(m => entities.HasComponent<ActiveNPCComponent>(m));
            foreach (var hero in heroes)
            {
                var combat = entities.GetComponent<CFCombatantComponent>(hero);
                damage += combat.MaxHp - combat.CurrentHp;
            }

            entities.System<SharedMapSystem>().DeleteMap(mapId);
        });
        await Server.WaitRunTicks(5);

        var perSystem = systems
            .Select(t => (System: t.Name, Ms: (after[t.Name] - before[t.Name]) * 1000 / MeasuredTicks))
            .Where(s => s.Ms > 0.001)
            .OrderByDescending(s => s.Ms)
            .ToList();
        var ai = perSystem.Where(s => IsAi(s.System)).Sum(s => s.Ms);

        ticks.Sort();
        return new Result(
            mode,
            fight,
            count,
            ticks.Average(),
            ticks[(int) (ticks.Count * 0.95)],
            ticks[^1],
            ai,
            awake,
            damage,
            perSystem);
    }

    /// <summary>NPC and GOAP systems. GOAP thinks inside <c>NPCSystem</c>, through HTN, and steers inside
    /// <c>NPCSteeringSystem</c>, so those two hold most of its cost.</summary>
    private static bool IsAi(string system)
    {
        return system.StartsWith("NPC") || system.StartsWith("HTN") || system.StartsWith("CFGOAP");
    }

    private List<Vector2i> FreeTiles(Entity<MapGridComponent> grid)
    {
        var maps = SEntMan.System<SharedMapSystem>();
        var lookup = SEntMan.System<EntityLookupSystem>();
        var free = new List<Vector2i>();
        var bodies = new HashSet<Entity<PhysicsComponent>>();
        foreach (var tile in maps.GetAllTiles(grid.Owner, grid.Comp))
        {
            bodies.Clear();
            var area = new Box2(tile.GridIndices, tile.GridIndices + Vector2i.One).Enlarged(-0.1f);
            lookup.GetLocalEntitiesIntersecting(grid.Owner, area, bodies, LookupFlags.Static | LookupFlags.Sensors);
            if (bodies.Any(b => b.Comp.CanCollide && b.Comp.Hard || SEntMan.HasComponent<CFDangerousTileComponent>(b)))
                continue;

            free.Add(tile.GridIndices);
        }

        return free.OrderBy(t => t.X).ThenBy(t => t.Y).ToList();
    }

    /// <summary>Within 6 tiles, and not on the hero's own tile.</summary>
    private static bool Near(Vector2i tile, Vector2i hero)
    {
        var offset = tile - hero;
        var squared = offset.X * offset.X + offset.Y * offset.Y;
        return squared is > 0 and <= 36;
    }

    private static EntityCoordinates Centre(EntityUid grid, Vector2i tile)
    {
        return new EntityCoordinates(grid, tile.X + 0.5f, tile.Y + 0.5f);
    }

    /// <summary>
    /// Seconds each system has spent updating, so far. This is the engine's own histogram: asking prometheus for it
    /// again with the same settings returns the one already made.
    /// </summary>
    private static Dictionary<string, double> Snapshot(List<Type> systems)
    {
        var histogram = Metrics.CreateHistogram(
            "robust_entity_systems_update_usage",
            "Amount of time spent processing each entity system",
            new HistogramConfiguration
            {
                LabelNames = new[] { "system" },
                Buckets = Histogram.ExponentialBuckets(0.000_001, 1.5, 25),
            });

        var sums = new Dictionary<string, double>();
        foreach (var type in systems)
        {
            sums[type.Name] = histogram.WithLabels(type.Name).Sum;
        }

        return sums;
    }

    private static string Line(Result r)
    {
        return FormattableString.Invariant(
            $"{r.Mode,-10} {(r.Fight ? "fight" : "idle"),-5} {r.Mobs,3} mobs: tick {r.AvgMs,6:F2} ms avg, {r.P95Ms,6:F2} p95, {r.MaxMs,7:F2} max, AI {r.AiMs,6:F3} ms, {r.Awake,3} awake, {r.HeroDamage,6} hero damage");
    }

    private static string Report(List<Result> results)
    {
        var text = new StringBuilder();
        text.AppendLine(FormattableString.Invariant($"GOAP stress test: {MeasuredTicks} ticks after {WarmupTicks} of warmup, {Heroes} heroes in a fight. A tick has {TickBudgetMs:F1} ms at 30 ticks a second."));
        text.AppendLine();
        text.AppendLine("| Mode | Scenario | Mobs | Tick avg ms | Tick p95 ms | Tick max ms | AI ms per tick | AI share of budget | Awake | Hero damage |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results)
        {
            text.AppendLine(FormattableString.Invariant(
                $"| {r.Mode} | {(r.Fight ? "fight" : "idle")} | {r.Mobs} | {r.AvgMs:F2} | {r.P95Ms:F2} | {r.MaxMs:F2} | {r.AiMs:F3} | {r.AiMs / TickBudgetMs:P1} | {r.Awake} | {r.HeroDamage} |"));
        }

        foreach (var r in results.Where(r => r.Mode != Mode.None))
        {
            text.AppendLine();
            text.AppendLine($"{r.Mode}, {(r.Fight ? "fight" : "idle")}, {r.Mobs} mobs. Top systems, ms per tick:");
            foreach (var (system, ms) in r.Systems.Take(12))
            {
                text.AppendLine(FormattableString.Invariant($"  {system,-40} {ms,8:F3}{(IsAi(system) ? "  (AI)" : "")}"));
            }
        }

        return text.ToString();
    }
}
