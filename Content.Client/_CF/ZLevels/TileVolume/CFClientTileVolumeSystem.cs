// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.CF.ZLevels.TileVolume;

/// <summary>
/// Owns <see cref="CFTileVolumeOverlay"/> and the per-grid <see cref="CFTileVolumeCacheComponent"/>:
/// builds chunk edge masks on demand and drops them when tiles change.
/// </summary>
public sealed partial class CFClientTileVolumeSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ITileDefinitionManager _tileDef = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>
    /// Scratch buffer for <see cref="BuildChunk"/>, not state: cleared before every use.
    /// </summary>
    private readonly ulong[] _maskScratch = new ulong[CFTileVolumeDirection.Count];

    public override void Initialize()
    {
        base.Initialize();
        _cfg.OnValueChanged(CFCVars.ZLevelsRenderingTileVolume, OnEnabledChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<CFTileVolumeOverlay>();
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (enabled)
        {
            if (!_overlay.HasOverlay<CFTileVolumeOverlay>())
                _overlay.AddOverlay(new CFTileVolumeOverlay());
        }
        else
        {
            _overlay.RemoveOverlay<CFTileVolumeOverlay>();
        }
    }

    [SubscribeLocalEvent]
    private void OnTileChanged(Entity<CFTileVolumeCacheComponent> ent, ref TileChangedEvent args)
    {
        if (args.Changes.Length == 0)
        {
            ent.Comp.Chunks.Clear();
            return;
        }

        // A tile change also flips the edge bits of its neighbours, which may live in adjacent chunks.
        foreach (var change in args.Changes)
        {
            ent.Comp.Chunks.Remove(SharedMapSystem.GetChunkIndices(change.GridIndices, CFTileVolumeChunk.Size));

            foreach (var offset in CFTileVolumeDirection.Offsets)
            {
                ent.Comp.Chunks.Remove(SharedMapSystem.GetChunkIndices(change.GridIndices + offset, CFTileVolumeChunk.Size));
            }
        }
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<ContentTileDefinition>())
            return;

        if (_overlay.TryGetOverlay<CFTileVolumeOverlay>(out var overlay))
            overlay.ClearVolumeSprites();

        var query = EntityQueryEnumerator<CFTileVolumeCacheComponent>();
        while (query.MoveNext(out var cache))
        {
            cache.Chunks.Clear();
        }
    }

    public CFTileVolumeCacheComponent EnsureCache(EntityUid grid)
    {
        return EnsureComp<CFTileVolumeCacheComponent>(grid);
    }

    public CFTileVolumeChunk GetChunk(Entity<MapGridComponent> grid, CFTileVolumeCacheComponent cache, Vector2i chunkIndices)
    {
        if (cache.Chunks.TryGetValue(chunkIndices, out var chunk))
            return chunk;

        chunk = BuildChunk(grid, chunkIndices);
        cache.Chunks[chunkIndices] = chunk;
        return chunk;
    }

    private CFTileVolumeChunk BuildChunk(Entity<MapGridComponent> grid, Vector2i chunkIndices)
    {
        var origin = chunkIndices * CFTileVolumeChunk.Size;
        var masks = _maskScratch;
        Array.Clear(masks);
        int[]? tileIds = null;

        for (var y = 0; y < CFTileVolumeChunk.Size; y++)
        {
            for (var x = 0; x < CFTileVolumeChunk.Size; x++)
            {
                var indices = origin + new Vector2i(x, y);
                if (!_map.TryGetTileRef(grid.Owner, grid.Comp, indices, out var tileRef) ||
                    CFZLevelOpeningCache.IsOpeningTile(tileRef.Tile, _tileDef))
                {
                    continue;
                }

                var bitIndex = y * CFTileVolumeChunk.Size + x;
                var bit = 1UL << bitIndex;
                var hasLip = false;

                for (var dir = 0; dir < CFTileVolumeDirection.Count; dir++)
                {
                    if (!CFZLevelOpeningCache.IsOpeningTile(grid, indices + CFTileVolumeDirection.Offsets[dir], _map, _tileDef))
                        continue;

                    masks[dir] |= bit;
                    hasLip = true;
                }

                if (!hasLip)
                    continue;

                tileIds ??= new int[CFTileVolumeChunk.Size * CFTileVolumeChunk.Size];
                tileIds[bitIndex] = tileRef.Tile.TypeId;
            }
        }

        if (tileIds == null)
            return CFTileVolumeChunk.Empty;

        var chunk = new CFTileVolumeChunk(tileIds);
        Array.Copy(masks, chunk.Masks, CFTileVolumeDirection.Count);
        return chunk;
    }
}
