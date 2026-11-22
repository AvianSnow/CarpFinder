using Content.Shared.EntityTable;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Station.Components;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.CF.Adventure;

/// <summary>
/// Spawns a wandering mob now and then, on grass or cave floor, out of sight of every player.
/// </summary>
public sealed partial class CFWildSpawnerSystem : EntitySystem
{
    /// <summary>Random tiles tried before a spawn gives up until the next interval.</summary>
    private const int Attempts = 50;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ITileDefinitionManager _tileDef = default!;
    [Dependency] private EntityTableSystem _entityTable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFWildSpawnerComponent, StationDataComponent>();
        while (query.MoveNext(out var uid, out var spawner, out var station))
        {
            if (spawner.NextSpawn == TimeSpan.Zero)
            {
                spawner.NextSpawn = now + spawner.Interval;
                continue;
            }

            if (now < spawner.NextSpawn)
                continue;

            spawner.NextSpawn = now + spawner.Interval;
            TrySpawn((uid, spawner), station.Grids, PlayerPositions());
        }
    }

    /// <summary>
    /// Spawns one roll of the table on a valid tile of <paramref name="grids"/>, away from <paramref name="players"/>.
    /// Returns what it spawned, or an empty list when there was no room or the cap is reached.
    /// </summary>
    public List<EntityUid> TrySpawn(Entity<CFWildSpawnerComponent> ent, IEnumerable<EntityUid> grids, List<MapCoordinates> players)
    {
        var spawned = new List<EntityUid>();
        ent.Comp.Spawned.RemoveAll(mob => TerminatingOrDeleted(mob) || _mobState.IsDead(mob));
        if (ent.Comp.Spawned.Count >= ent.Comp.MaxAlive)
            return spawned;

        var candidates = new List<(EntityUid Grid, MapGridComponent Comp, Vector2i Tile)>();
        foreach (var grid in grids)
        {
            if (!TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            var tiles = _maps.GetAllTilesEnumerator(grid, gridComp);
            while (tiles.MoveNext(out var tileRef))
            {
                if (ent.Comp.Tiles.Contains(_tileDef[tileRef.Value.Tile.TypeId].ID))
                    candidates.Add((grid, gridComp, tileRef.Value.GridIndices));
            }
        }

        for (var i = 0; i < Attempts && candidates.Count > 0; i++)
        {
            var index = _random.Next(candidates.Count);
            var (grid, gridComp, tile) = candidates[index];
            candidates.RemoveSwap(index);

            if (_turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable | CollisionGroup.MidImpassable, gridComp))
                continue;

            var coords = _maps.GridTileToLocal(grid, gridComp, tile);
            if (NearPlayer(_transform.ToMapCoordinates(coords), players, ent.Comp.MinPlayerDistance))
                continue;

            foreach (var id in _entityTable.GetSpawns(ent.Comp.Table))
            {
                spawned.Add(SpawnAtPosition(id, coords));
            }

            ent.Comp.Spawned.AddRange(spawned);
            return spawned;
        }

        return spawned;
    }

    /// <summary>Where each player in a living or dying body is. Ghosts do not count.</summary>
    public List<MapCoordinates> PlayerPositions()
    {
        var positions = new List<MapCoordinates>();
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is { } body && HasComp<MobStateComponent>(body) && !_mobState.IsDead(body))
                positions.Add(_transform.GetMapCoordinates(body));
        }

        return positions;
    }

    private static bool NearPlayer(MapCoordinates point, List<MapCoordinates> players, float distance)
    {
        foreach (var player in players)
        {
            if (player.MapId == point.MapId && (player.Position - point.Position).LengthSquared() < distance * distance)
                return true;
        }

        return false;
    }
}
