using System.Numerics;
using Content.Shared.CF.Effects;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Runs breaths that come out as gas (<see cref="CFBreathGasComponent"/>). Every step the breather lets out more gas
/// on the tiles beside it, then each tile shares with its neighbours as atmos does, over the area's reachable tiles
/// only. Each tile's cloud is drawn thicker the more gas it holds. Anyone standing on a filled tile rolls once against
/// the breath. Server only: the client sees the cloud entities.
/// </summary>
public sealed partial class CFBreathGasSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFBreathFlowSystem _flow = default!;

    /// <summary>How many thicknesses a tile of cloud is drawn in.</summary>
    public const int Levels = 6;

    /// <summary>The gas that is one step of thickness.</summary>
    private const float GasPerLevel = 0.2f;

    /// <summary>Less than this on every tile and the cloud is gone.</summary>
    private const float Gone = 0.02f;

    /// <summary>How much of the difference a tile shares with a neighbour each spread. Atmos-like, and stable below 0.25.</summary>
    private const float Share = 0.2f;

    private static readonly TimeSpan Step = TimeSpan.FromSeconds(0.1);

    /// <summary>
    /// Lets the breath out as gas at a point. False when it does not come out as gas, so the caller tries the next way.
    /// </summary>
    public bool TryStart(Entity<CFBreathGasComponent?> breath, EntityUid user, MapCoordinates aim)
    {
        if (!Resolve(breath, ref breath.Comp, false))
            return false;

        if (!_net.IsServer)
            return true;

        if (!_flow.TryReach(breath, user, aim, out var grid, out var from, out var reached))
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-no-one"), user, user);
            return true;
        }

        var gas = breath.Comp;
        var now = _timing.CurTime;
        var cloud = new CFBreathCloudComponent
        {
            Source = breath,
            User = user,
            Grid = grid,
            Cloud = gas.Cloud,
            EmitSeconds = gas.EmitSeconds,
            SpreadsPerSecond = gas.SpreadsPerSecond,
            Filled = gas.Filled,
            LingerSeconds = gas.LingerSeconds,
            FadePerSecond = gas.FadePerSecond,
            Start = now,
            NextStep = now,
        };

        for (var i = 0; i < reached.Count; i++)
        {
            var tile = reached[i];
            cloud.Tiles.Add(tile);
            cloud.Gas.Add(0f);
            cloud.Shown.Add(0);
            cloud.Clouds.Add(null);
            if (Math.Abs(tile.X - from.X) <= 1 && Math.Abs(tile.Y - from.Y) <= 1)
                cloud.Mouth.Add(i);
        }

        // Walled in so that nothing beside the breather is open, but the area still reaches round: start from the first.
        if (cloud.Mouth.Count == 0)
            cloud.Mouth.Add(0);

        var uid = Spawn();
        AddComp(uid, cloud);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFBreathCloudComponent>();
        while (query.MoveNext(out var uid, out var cloud))
        {
            if (!TryComp(cloud.Grid, out MapGridComponent? gridComp))
            {
                End(uid, cloud);
                continue;
            }

            // Catch up at most a few steps, so a long stall does not run the whole cloud in one tick.
            for (var steps = 0; steps < 3 && now >= cloud.NextStep; steps++)
            {
                cloud.NextStep += Step;
                if (Simulate((cloud.Grid, gridComp), cloud))
                    continue;

                End(uid, cloud);
                break;
            }
        }
    }

    /// <summary>One step of the cloud. False when it has all gone.</summary>
    private bool Simulate(Entity<MapGridComponent> grid, CFBreathCloudComponent cloud)
    {
        var dt = (float) Step.TotalSeconds;
        var elapsed = (float) (cloud.NextStep - cloud.Start).TotalSeconds;
        var count = cloud.Tiles.Count;

        // The breather lets out one settled tile of gas per tile the cloud can cover, over EmitSeconds.
        if (elapsed <= cloud.EmitSeconds + dt / 2f && cloud.EmitSeconds > 0f)
        {
            var released = count * dt / cloud.EmitSeconds / cloud.Mouth.Count;
            foreach (var mouth in cloud.Mouth)
            {
                cloud.Gas[mouth] += released;
            }
        }
        else if (cloud.EmitSeconds <= 0f && elapsed <= dt)
        {
            foreach (var mouth in cloud.Mouth)
            {
                cloud.Gas[mouth] += (float) count / cloud.Mouth.Count;
            }
        }

        Spread(cloud, Math.Max(1, (int) MathF.Round(cloud.SpreadsPerSecond * dt)));

        if (elapsed >= cloud.LingerSeconds)
        {
            var keep = Math.Max(0f, 1f - cloud.FadePerSecond * dt);
            for (var i = 0; i < count; i++)
            {
                cloud.Gas[i] *= keep;
            }
        }

        var filled = new HashSet<Vector2i>();
        var any = false;
        for (var i = 0; i < count; i++)
        {
            var amount = cloud.Gas[i];
            if (amount >= Gone)
                any = true;

            if (amount >= cloud.Filled)
                filled.Add(cloud.Tiles[i]);

            Show(grid, cloud, i);
        }

        Catch(grid, cloud, filled);
        return any || elapsed <= cloud.EmitSeconds;
    }

    /// <summary>Each tile shares with the tiles beside it, as atmos does, all at once so the order does not matter.</summary>
    private static void Spread(CFBreathCloudComponent cloud, int times)
    {
        var count = cloud.Tiles.Count;
        var index = new Dictionary<Vector2i, int>(count);
        for (var i = 0; i < count; i++)
        {
            index[cloud.Tiles[i]] = i;
        }

        var change = new float[count];
        for (var time = 0; time < times; time++)
        {
            Array.Clear(change, 0, change.Length);
            for (var i = 0; i < count; i++)
            {
                var tile = cloud.Tiles[i];

                // Only east and north, so each pair of neighbours is counted once.
                if (index.TryGetValue(tile + new Vector2i(1, 0), out var east))
                    Exchange(cloud.Gas, change, i, east);

                if (index.TryGetValue(tile + new Vector2i(0, 1), out var north))
                    Exchange(cloud.Gas, change, i, north);
            }

            for (var i = 0; i < count; i++)
            {
                cloud.Gas[i] = Math.Max(0f, cloud.Gas[i] + change[i]);
            }
        }
    }

    private static void Exchange(List<float> gas, float[] change, int a, int b)
    {
        var flow = Share * (gas[a] - gas[b]);
        change[a] -= flow;
        change[b] += flow;
    }

    private void Show(Entity<MapGridComponent> grid, CFBreathCloudComponent cloud, int i)
    {
        var amount = cloud.Gas[i];
        var level = amount < Gone ? 0 : Math.Clamp((int) MathF.Ceiling(amount / GasPerLevel), 1, Levels);
        if (level == cloud.Shown[i])
            return;

        cloud.Shown[i] = level;
        if (cloud.Clouds[i] is not { } tileCloud || TerminatingOrDeleted(tileCloud))
        {
            if (level == 0)
                return;

            var center = ((Vector2) cloud.Tiles[i] + new Vector2(0.5f, 0.5f)) * grid.Comp.TileSize;
            tileCloud = SpawnAttachedTo(cloud.Cloud, new EntityCoordinates(grid, center));
            cloud.Clouds[i] = tileCloud;
        }

        _appearance.SetData(tileCloud, CFBreathCloudVisuals.Density, level);
    }

    /// <summary>Anyone on a filled tile who has not rolled yet rolls now.</summary>
    private void Catch(Entity<MapGridComponent> grid, CFBreathCloudComponent cloud, HashSet<Vector2i> filled)
    {
        // The breath is out: it still lands if the breather has died since, but not if it is gone.
        if (filled.Count == 0 || TerminatingOrDeleted(cloud.Source) || TerminatingOrDeleted(cloud.User))
            return;

        foreach (var target in _effects.CreaturesInTiles(cloud.Source, cloud.User, grid, filled))
        {
            if (cloud.Caught.Add(target))
                _effects.ResolveTarget(cloud.Source, cloud.User, target);
        }
    }

    private void End(EntityUid uid, CFBreathCloudComponent cloud)
    {
        foreach (var tileCloud in cloud.Clouds)
        {
            if (tileCloud is { } tile)
                QueueDel(tile);
        }

        if (cloud.Caught.Count == 0 && !TerminatingOrDeleted(cloud.User))
            _popup.PopupEntity(Loc.GetString("cf-spell-no-one"), cloud.User, cloud.User);

        QueueDel(uid);
    }
}
