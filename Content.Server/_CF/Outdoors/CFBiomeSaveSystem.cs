using System.Collections.Generic;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Events;
using Robust.Shared.Maths;

namespace Content.Server.CF.Outdoors;

// This is the biome's own bookkeeping, which only its system may write. It does here what that system does when a chunk
// unloads, and upstream has no hook for it, so the access rule is set aside for this file.
#pragma warning disable RA0002

/// <summary>
/// Lets a planet map save after some of the biome's generated entities were deleted.
/// A <c>Biome</c> remembers what it spawned in <see cref="BiomeComponent.LoadedEntities"/>, keyed by entity, and
/// nothing takes a deleted one out until the chunk unloads. Saving writes every deleted key as <c>invalid</c>, and two
/// of them collide, so the map could not be saved. This does what the unload would have done: the tile is marked
/// modified, so the biome does not put the entity back, and the key is dropped.
/// </summary>
public sealed partial class CFBiomeSaveSystem : EntitySystem
{
    private readonly List<(EntityUid Entity, Vector2i Tile)> _gone = new();

    [SubscribeLocalEvent]
    private void OnBeforeSave(BeforeSerializationEvent ev)
    {
        var query = AllEntityQuery<BiomeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var biome, out var xform))
        {
            if (!ev.Entities.Contains(uid) && !ev.MapIds.Contains(xform.MapID))
                continue;

            foreach (var (chunk, loaded) in biome.LoadedEntities)
            {
                _gone.Clear();
                foreach (var (ent, tile) in loaded)
                {
                    if (TerminatingOrDeleted(ent))
                        _gone.Add((ent, tile));
                }

                if (_gone.Count == 0)
                    continue;

                if (!biome.ModifiedTiles.TryGetValue(chunk, out var modified))
                {
                    modified = new HashSet<Vector2i>();
                    biome.ModifiedTiles[chunk] = modified;
                }

                foreach (var (ent, tile) in _gone)
                {
                    loaded.Remove(ent);
                    modified.Add(tile);
                }
            }
        }

        _gone.Clear();
    }
}
