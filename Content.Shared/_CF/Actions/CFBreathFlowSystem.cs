using System.Numerics;
using Content.Shared.Chemistry.Components;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

/// <summary>
/// A breath that pours out as liquid (<see cref="CFBreathWeaponComponent.Reagent"/>), such as Toxic Spew. It floods the
/// area's open tiles from the breather outwards, as stock puddles, so it draws as one sheet and evaporates as any spill
/// does. It flows only through tiles it can reach from the breather without crossing a wall, a closed door or open sky.
/// When the farthest tile has filled, every creature standing in it rolls against the breath. Server only. A breath that
/// comes out as gas is <see cref="CFBreathGasSystem"/>.
/// </summary>
public sealed partial class CFBreathFlowSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedPuddleSystem _puddles = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFEffectSystem _effects = default!;

    private static readonly Vector2i[] Sides =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    /// <summary>
    /// Starts the breath flowing at a point. False when it does not pour as liquid, so the caller resolves it at once.
    /// </summary>
    public bool TryStart(Entity<CFBreathWeaponComponent> breath, EntityUid user, MapCoordinates aim)
    {
        if (breath.Comp.Reagent is not { } reagent)
            return false;

        if (!_net.IsServer)
            return true;

        if (!TryReach(breath, user, aim, out var grid, out var from, out var reached))
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-no-one"), user, user);
            return true;
        }

        var now = _timing.CurTime;
        var flowSeconds = Math.Max(0f, breath.Comp.FlowSeconds);
        var flow = new CFBreathFlowComponent
        {
            Source = breath,
            User = user,
            Grid = grid,
            Reagent = reagent,
            UnitsPerTile = breath.Comp.UnitsPerTile,
            Start = now,
            End = now + TimeSpan.FromSeconds(flowSeconds),
        };

        // The nearest tile fills at once and the farthest at the end, so it reads as a wave rolling out.
        var nearest = float.MaxValue;
        var farthest = 0f;
        foreach (var tile in reached)
        {
            var distance = ((Vector2) (tile - from)).Length();
            nearest = Math.Min(nearest, distance);
            farthest = Math.Max(farthest, distance);
        }

        var span = farthest - nearest;
        foreach (var tile in reached)
        {
            var distance = ((Vector2) (tile - from)).Length();
            var delay = span > 0.001f ? flowSeconds * (distance - nearest) / span : 0f;
            flow.Tiles.Add(new CFBreathFlowTile(tile, delay));
        }

        flow.Tiles.Sort((a, b) => a.Delay.CompareTo(b.Delay));

        var uid = Spawn();
        AddComp(uid, flow);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFBreathFlowComponent>();
        while (query.MoveNext(out var uid, out var flow))
        {
            if (!TryComp(flow.Grid, out MapGridComponent? gridComp))
            {
                QueueDel(uid);
                continue;
            }

            var grid = new Entity<MapGridComponent>(flow.Grid, gridComp);
            var elapsed = (float) (now - flow.Start).TotalSeconds;
            while (flow.Filled < flow.Tiles.Count && flow.Tiles[flow.Filled].Delay <= elapsed)
            {
                Pour(grid, flow, flow.Tiles[flow.Filled].Tile, sound: flow.Filled == 0);
                flow.Filled++;
            }

            if (now < flow.End || flow.Filled < flow.Tiles.Count)
                continue;

            QueueDel(uid);

            // The breath already left: it still lands if the breather has died since, but not if it is gone.
            if (TerminatingOrDeleted(flow.Source) || TerminatingOrDeleted(flow.User))
                continue;

            var tiles = new HashSet<Vector2i>();
            foreach (var tile in flow.Tiles)
            {
                tiles.Add(tile.Tile);
            }

            _effects.ResolveTiles(flow.Source, flow.User, grid, tiles);
        }
    }

    /// <summary>
    /// The tiles of a breath's area that something pouring out of the breather can get to, and the breather's own
    /// tile. False when it is not on a grid or every tile is walled off. Liquid and gas breaths both use this.
    /// </summary>
    public bool TryReach(
        EntityUid breath,
        EntityUid user,
        MapCoordinates aim,
        out Entity<MapGridComponent> grid,
        out Vector2i from,
        out List<Vector2i> reached)
    {
        from = default;
        reached = new List<Vector2i>();
        if (!_effects.AreaTiles(breath, user, aim, out grid, out var area))
            return false;

        from = CFAreaGrid.ToTile(
            _maps.WorldToLocal(grid, grid.Comp, _transform.GetWorldPosition(user)),
            grid.Comp.TileSize);
        reached = Reach(grid, from, area);
        return reached.Count > 0;
    }

    private void Pour(Entity<MapGridComponent> grid, CFBreathFlowComponent flow, Vector2i tile, bool sound)
    {
        var solution = new Solution(flow.Reagent, FixedPoint2.New(flow.UnitsPerTile));
        _puddles.TrySpillAt(_maps.GetTileRef(grid, grid.Comp, tile), solution, out _, sound: sound, tileReact: false);
    }

    /// <summary>
    /// The area's tiles the liquid can get to: it pours from the breather's tile into the area beside it, then spreads
    /// side to side, never diagonally, so it cannot slip between two walls that meet at a corner.
    /// </summary>
    private List<Vector2i> Reach(Entity<MapGridComponent> grid, Vector2i from, HashSet<Vector2i> area)
    {
        var reached = new List<Vector2i>();
        var seen = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                var tile = from + new Vector2i(x, y);
                if (area.Contains(tile) && seen.Add(tile) && Open(grid, tile))
                    queue.Enqueue(tile);
            }
        }

        while (queue.TryDequeue(out var tile))
        {
            reached.Add(tile);
            foreach (var side in Sides)
            {
                var next = tile + side;
                if (area.Contains(next) && seen.Add(next) && Open(grid, next))
                    queue.Enqueue(next);
            }
        }

        return reached;
    }

    /// <summary>Floor with nothing solid on it. Tables, creatures and open doors let liquid under them.</summary>
    private bool Open(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var tileRef = _maps.GetTileRef(grid, grid.Comp, tile);
        if (tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef))
            return false;

        return !_turf.IsTileBlocked(tileRef, CollisionGroup.Impassable);
    }
}
