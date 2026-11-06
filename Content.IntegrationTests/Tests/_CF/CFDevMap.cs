using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Shared.CF.GOAP;
using Content.Shared.Mobs.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.CF;

/// <summary>
/// Loads an arena for tests and finds open floor on it. CF tests use this instead of building grids by hand.
/// Keep a test's footprint as small as it needs: only a small one is sure of clear floor.
/// </summary>
public sealed class CFDevMap
{
    public static readonly ResPath Path = new("/Maps/Nonstations/meteor-arena.yml");

    public MapId MapId { get; private set; }

    public EntityUid Grid { get; private set; }

    /// <summary>A floor tile with the whole requested footprint around it free of creatures.</summary>
    public Vector2i Origin { get; private set; }

    /// <summary>
    /// Loads the map and picks floor where every tile of <paramref name="footprint"/> (relative to the origin,
    /// right and top exclusive) exists on the largest grid and has no creature on it.
    /// Pass <paramref name="avoidStructures"/> when a creature has to stand up there. It is off by default,
    /// because the map has no structure-free room for the large area spell footprints.
    /// </summary>
    public static async Task<CFDevMap> Load(TestPair pair, Box2i footprint, bool avoidStructures = false)
    {
        var result = new CFDevMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitPost(() =>
        {
            var loader = entities.System<MapLoaderSystem>();
            var options = new DeserializationOptions { InitializeMaps = true };
            Assert.That(loader.TryLoadMap(Path, out var map, out var grids, options), $"Failed to load {Path}");
            result.MapId = map!.Value.Comp.MapId;

            var maps = entities.System<SharedMapSystem>();
            var lookup = entities.System<EntityLookupSystem>();
            var grid = grids!.MaxBy(g => maps.GetAllTiles(g.Owner, g.Comp).Count());
            result.Grid = grid.Owner;

            var floor = new HashSet<Vector2i>();
            foreach (var tile in maps.GetAllTiles(grid.Owner, grid.Comp))
            {
                floor.Add(tile.GridIndices);
            }

            // Clear floor first, so nothing stands between two test creatures. Only a footprint too big for that
            // settles for floor with structures on it, and a creature that must stand there says so.
            foreach (var clear in avoidStructures ? new[] { true } : new[] { true, false })
            {
                foreach (var candidate in floor.OrderBy(t => t.X).ThenBy(t => t.Y))
                {
                    if (!Fits(candidate, floor, footprint))
                        continue;

                    // Shrunk a little, or a tile only touching the edge counts as being inside it.
                    var area = new Box2(candidate + footprint.BottomLeft, candidate + footprint.TopRight).Enlarged(-0.1f);
                    var mobs = new HashSet<Entity<MobStateComponent>>();
                    lookup.GetLocalEntitiesIntersecting(grid.Owner, area, mobs);
                    if (mobs.Count != 0)
                        continue;

                    if (clear)
                    {
                        var solids = new HashSet<Entity<PhysicsComponent>>();
                        lookup.GetLocalEntitiesIntersecting(grid.Owner, area, solids, LookupFlags.Static);
                        if (solids.Any(s => s.Comp.CanCollide && s.Comp.Hard))
                            continue;

                        // The arena has lava around it, which would set test creatures alight.
                        var hazards = new HashSet<Entity<CFDangerousTileComponent>>();
                        lookup.GetLocalEntitiesIntersecting(grid.Owner, area, hazards, LookupFlags.Static | LookupFlags.Sensors);
                        if (hazards.Count != 0)
                            continue;
                    }

                    result.Origin = candidate;
                    return;
                }
            }

            Assert.Fail($"{Path} has no open floor of {footprint.Width}x{footprint.Height} tiles.");
        });

        return result;
    }

    /// <summary>The center of the tile (x, y) away from <see cref="Origin"/>, in grid space.</summary>
    public EntityCoordinates Local(int x, int y)
    {
        return new EntityCoordinates(Grid, Origin.X + x + 0.5f, Origin.Y + y + 0.5f);
    }

    private static bool Fits(Vector2i origin, HashSet<Vector2i> floor, Box2i footprint)
    {
        for (var x = footprint.Left; x < footprint.Right; x++)
        {
            for (var y = footprint.Bottom; y < footprint.Top; y++)
            {
                if (!floor.Contains(origin + new Vector2i(x, y)))
                    return false;
            }
        }

        return true;
    }
}
