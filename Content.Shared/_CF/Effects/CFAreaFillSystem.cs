using System.Numerics;
using Content.Shared.Chemistry.Components;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Runs <see cref="CFAreaFillComponent"/>: an area that fills with flames, sparks or puddles. It works out which of
/// the area's tiles can be reached from where the area starts without crossing a wall, puts the effect on them nearest
/// first, and has the creatures on them roll. A lingering fill then watches who moves on it. Server only: clients see
/// the tile effects and puddles. Gas clouds are <c>CFBreathGasSystem</c>, which uses <see cref="TryReach"/> too.
/// </summary>
public sealed partial class CFAreaFillSystem : EntitySystem
{
    private static readonly TimeSpan WatchEvery = TimeSpan.FromSeconds(0.2);

    /// <summary>How much faster than its walk a creature has to be going to count as more than a Step.</summary>
    private const float WalkSlack = 0.3f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedPuddleSystem _puddles = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFVfxSystem _vfx = default!;

    private static readonly Vector2i[] Sides =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    /// <summary>
    /// Fills the source's area. False when the source has no fill, so the caller resolves the area the plain way.
    /// </summary>
    /// <param name="affected">How many creatures rolled at once.</param>
    public bool TryStart(EntityUid source, EntityUid user, MapCoordinates aim, out int affected)
    {
        affected = 0;
        if (!TryComp(source, out CFAreaFillComponent? fill))
            return false;

        if (!_net.IsServer)
            return true;

        if (!TryReach(source, user, aim, fill.Fill.Reagent != null, out var grid, out var from, out var reached))
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-no-one"), user, user);
            return true;
        }

        var active = Begin(source, user, grid, from, reached, fill.Fill);
        if (fill.Rolls == CFFillRolls.WhenFilled)
        {
            active.RollWhenFilled = true;
            return true;
        }

        affected = Roll(active, grid, reached);
        return true;
    }

    /// <summary>Something only seen, around a point: what a bomb or a glob of acid leaves where it lands.</summary>
    public void Splash(EntityUid source, EntityUid user, MapCoordinates center, CFAreaShape shape, int sizeFeet, CFFill fill)
    {
        if (!_net.IsServer || !_maps.TryFindGridAt(center, out var gridUid, out var gridComp))
            return;

        var local = _maps.WorldToLocal(gridUid, gridComp, center.Position);
        var area = CFAreaGrid.Collect(shape, sizeFeet, local, local, gridComp.TileSize, includeSelf: true);
        var from = CFAreaGrid.ToTile(local, gridComp.TileSize);
        var grid = new Entity<MapGridComponent>(gridUid, gridComp);
        var reached = Reach(grid, from, area, fill.Reagent != null);
        if (reached.Count == 0)
            return;

        var active = Begin(null, user, grid, from, reached, fill);
        if (fill.Tint && !TerminatingOrDeleted(source))
            active.Color = _vfx.SpellColor(source);
    }

    private CFActiveFillComponent Begin(
        EntityUid? source,
        EntityUid user,
        Entity<MapGridComponent> grid,
        Vector2i from,
        List<Vector2i> reached,
        CFFill fill)
    {
        var now = _timing.CurTime;
        var spread = Math.Max(0f, fill.SpreadSeconds);
        var active = new CFActiveFillComponent
        {
            Source = source,
            User = user,
            Grid = grid,
            Fill = fill,
            Start = now,
            End = now + TimeSpan.FromSeconds(Math.Max(spread, fill.LingerSeconds)),
            NextWatch = now,
        };

        if (fill.Tint && source is { } tinted)
            active.Color = _vfx.SpellColor(tinted);

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
            var delay = span > 0.001f ? spread * (distance - nearest) / span : 0f;
            active.Tiles.Add(new CFFillTile(tile, delay));
        }

        active.Tiles.Sort((a, b) => a.Delay.CompareTo(b.Delay));

        var uid = Spawn();
        AddComp(uid, active);

        // What is due now appears in this tick, not the next.
        Advance(grid, active, now);
        return active;
    }

    /// <summary>Everyone standing on the tiles rolls against the source.</summary>
    private int Roll(CFActiveFillComponent active, Entity<MapGridComponent> grid, IReadOnlyCollection<Vector2i> tiles)
    {
        if (active.Source is not { } source || TerminatingOrDeleted(source) || TerminatingOrDeleted(active.User))
            return 0;

        var targets = _effects.CreaturesInTiles(source, active.User, grid, tiles);
        var again = _timing.CurTime + TimeSpan.FromSeconds(active.Fill.RerollSeconds);
        foreach (var target in targets)
        {
            active.NextRoll[target] = again;
            _effects.ResolveTarget(source, active.User, target);
        }

        if (targets.Count == 0)
            _popup.PopupEntity(Loc.GetString("cf-spell-no-one"), active.User, active.User);

        return targets.Count;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFActiveFillComponent>();
        while (query.MoveNext(out var uid, out var active))
        {
            if (!TryComp(active.Grid, out MapGridComponent? gridComp))
            {
                Finish(uid, active);
                continue;
            }

            var grid = new Entity<MapGridComponent>(active.Grid, gridComp);
            Advance(grid, active, now);
            if (active.Filled < active.Tiles.Count)
                continue;

            if (active.RollWhenFilled)
            {
                active.RollWhenFilled = false;
                var tiles = new HashSet<Vector2i>();
                foreach (var tile in active.Tiles)
                {
                    tiles.Add(tile.Tile);
                }

                Roll(active, grid, tiles);
            }

            if (active.Fill.LingerSeconds <= 0f)
            {
                // Flames burn out and puddles dry up by themselves.
                QueueDel(uid);
                continue;
            }

            if (now >= active.End || active.Source is { } source && TerminatingOrDeleted(source))
            {
                Finish(uid, active);
                continue;
            }

            if (now < active.NextWatch)
                continue;

            active.NextWatch = now + WatchEvery;
            Watch(grid, active, now);
        }
    }

    private void Advance(Entity<MapGridComponent> grid, CFActiveFillComponent active, TimeSpan now)
    {
        var elapsed = (float) (now - active.Start).TotalSeconds;
        while (active.Filled < active.Tiles.Count && active.Tiles[active.Filled].Delay <= elapsed)
        {
            Place(grid, active, active.Tiles[active.Filled].Tile, sound: active.Filled == 0);
            active.Filled++;
        }
    }

    private void Place(Entity<MapGridComponent> grid, CFActiveFillComponent active, Vector2i tile, bool sound)
    {
        var fill = active.Fill;
        if (fill.Tile is { } effect)
        {
            var center = ((Vector2) tile + new Vector2(0.5f, 0.5f)) * grid.Comp.TileSize;
            var spawned = SpawnAttachedTo(effect, new EntityCoordinates(grid, center));
            if (active.Color is { } color)
            {
                var vfx = EnsureComp<CFVfxComponent>(spawned);
                vfx.Color = color;
                Dirty(spawned, vfx);
            }

            if (fill.LingerSeconds > 0f)
                active.Spawned.Add(spawned);
        }

        if (fill.Reagent is not { } reagent)
            return;

        var solution = new Solution(reagent, FixedPoint2.New(fill.UnitsPerTile));
        if (!_puddles.TrySpillAt(_maps.GetTileRef(grid, grid.Comp, tile), solution, out var puddle, sound: sound, tileReact: false))
            return;

        if (fill.LingerSeconds > 0f)
            active.Puddles[tile] = puddle;
    }

    /// <summary>A creature going faster than a walk on a lingering fill rolls against it, as it did when it was cast.</summary>
    private void Watch(Entity<MapGridComponent> grid, CFActiveFillComponent active, TimeSpan now)
    {
        if (active.Source is not { } source || TerminatingOrDeleted(active.User))
            return;

        var tiles = new HashSet<Vector2i>();
        foreach (var tile in active.Tiles)
        {
            // A puddle someone mopped up is no longer part of it.
            if (active.Fill.Reagent != null
                && (!active.Puddles.TryGetValue(tile.Tile, out var puddle) || TerminatingOrDeleted(puddle)))
            {
                continue;
            }

            tiles.Add(tile.Tile);
        }

        foreach (var target in _effects.CreaturesInTiles(source, active.User, grid, tiles))
        {
            if (active.NextRoll.TryGetValue(target, out var next) && now < next)
                continue;

            if (!TryComp(target, out PhysicsComponent? body))
                continue;

            var walk = CompOrNull<MovementSpeedModifierComponent>(target)?.CurrentWalkSpeed ?? 2.5f;
            if (body.LinearVelocity.Length() <= walk + WalkSlack)
                continue;

            active.NextRoll[target] = now + TimeSpan.FromSeconds(active.Fill.RerollSeconds);
            _effects.ResolveTarget(source, active.User, target);
        }
    }

    private void Finish(EntityUid uid, CFActiveFillComponent active)
    {
        foreach (var spawned in active.Spawned)
        {
            QueueDel(spawned);
        }

        foreach (var puddle in active.Puddles.Values)
        {
            QueueDel(puddle);
        }

        QueueDel(uid);
    }

    /// <summary>
    /// The tiles of a source's area that something spreading from where the area starts can get to, and the tile it
    /// starts from: the user's for a cone, line or emanation, the aimed one for a burst or square. False when the user
    /// is not on a grid or every tile is walled off.
    /// </summary>
    /// <param name="needsFloor">Liquid and heavy gas do not cross open sky.</param>
    public bool TryReach(
        EntityUid source,
        EntityUid user,
        MapCoordinates aim,
        bool needsFloor,
        out Entity<MapGridComponent> grid,
        out Vector2i from,
        out List<Vector2i> reached)
    {
        from = default;
        reached = new List<Vector2i>();
        if (!_effects.AreaTiles(source, user, aim, out grid, out var area))
            return false;

        var start = Comp<CFAreaEffectComponent>(source).StartsAtPoint() ? aim.Position : _transform.GetWorldPosition(user);
        from = CFAreaGrid.ToTile(_maps.WorldToLocal(grid, grid.Comp, start), grid.Comp.TileSize);
        reached = Reach(grid, from, area, needsFloor);
        return reached.Count > 0;
    }

    /// <summary>
    /// It starts on the first tile and the area beside it, then spreads side to side, never diagonally, so it cannot
    /// slip between two walls that meet at a corner.
    /// </summary>
    private List<Vector2i> Reach(Entity<MapGridComponent> grid, Vector2i from, HashSet<Vector2i> area, bool needsFloor)
    {
        var reached = new List<Vector2i>();
        var seen = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                var tile = from + new Vector2i(x, y);
                if (area.Contains(tile) && seen.Add(tile) && Open(grid, tile, needsFloor))
                    queue.Enqueue(tile);
            }
        }

        while (queue.TryDequeue(out var tile))
        {
            reached.Add(tile);
            foreach (var side in Sides)
            {
                var next = tile + side;
                if (area.Contains(next) && seen.Add(next) && Open(grid, next, needsFloor))
                    queue.Enqueue(next);
            }
        }

        return reached;
    }

    /// <summary>Nothing solid on it. Tables, creatures and open doors let it past.</summary>
    private bool Open(Entity<MapGridComponent> grid, Vector2i tile, bool needsFloor)
    {
        var tileRef = _maps.GetTileRef(grid, grid.Comp, tile);
        if (needsFloor && (tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef)))
            return false;

        return !_turf.IsTileBlocked(tileRef, CollisionGroup.Impassable);
    }
}
