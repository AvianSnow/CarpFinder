// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.CF.ZLevels.Roof;

/// <summary>
/// Systems that automatically covers tiles with roofs (or removes roofs)
/// if there is a tile on one of the levels above in the ZLevels Map network.
/// Roof data is calculated on the server only and networked to clients.
/// </summary>
public abstract partial class CFSharedZLevelsRoofSystem : EntitySystem
{
    [Dependency] protected CFSharedZLevelsSystem ZLevel = null!;
    [Dependency] protected SharedRoofSystem Roof = null!;
    [Dependency] protected SharedMapSystem Map = null!;
    [Dependency] protected ITileDefinitionManager TilDefMan = null!;

    [Dependency] protected EntityQuery<MapGridComponent> GridQuery = default!;
    [Dependency] protected EntityQuery<RoofComponent> RoofQuery = default!;
    [Dependency] protected EntityQuery<CFZLevelRoofComponent> ZRoofQuery = default!;
    [Dependency] protected EntityQuery<CFZMapComponent> ZMapQuery = default!;
    [Dependency] protected EntityQuery<TransformComponent> XformQuery = default!;

    /// <summary>
    /// Whether a tile on one of the z-levels above shields this tile from weather.
    /// </summary>
    public bool IsWeatherBlocked(Entity<CFZLevelRoofComponent?> ent, Vector2i index)
    {
        if (!ZRoofQuery.Resolve(ent, ref ent.Comp, false))
            return false;

        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, CFZLevelRoofComponent.ChunkSize);
        if (!ent.Comp.WeatherData.TryGetValue(chunkOrigin, out var bitMask))
            return false;

        var bitFlag = GetBitFlag(index);
        return (bitMask & bitFlag) == bitFlag;
    }

    protected void SetWeatherBlocked(Entity<CFZLevelRoofComponent> ent, Vector2i index, bool value)
    {
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, CFZLevelRoofComponent.ChunkSize);

        if (!ent.Comp.WeatherData.TryGetValue(chunkOrigin, out var chunkData))
        {
            if (!value)
                return;

            chunkData = 0;
        }

        var bitFlag = GetBitFlag(index);
        var newData = value ? chunkData | bitFlag : chunkData & ~bitFlag;
        if (newData == chunkData)
            return;

        ent.Comp.WeatherData[chunkOrigin] = newData;
        Dirty(ent);
    }

    /// <summary>
    /// Whether this tile casts a roof shadow on the z-levels below.
    /// </summary>
    protected bool CastsShadow(Tile tile)
    {
        return !tile.IsEmpty && !((ContentTileDefinition)TilDefMan[tile.TypeId]).Transparent;
    }

    /// <summary>
    /// Whether this tile shields the z-levels below from weather.
    /// </summary>
    protected bool BlocksWeather(Tile tile)
    {
        return !tile.IsEmpty && ((ContentTileDefinition)TilDefMan[tile.TypeId]).BlocksWeather;
    }

    private static ulong GetBitFlag(Vector2i index)
    {
        var chunkRelative = SharedMapSystem.GetChunkRelative(index, CFZLevelRoofComponent.ChunkSize);
        return (ulong) 1 << (chunkRelative.X + chunkRelative.Y * CFZLevelRoofComponent.ChunkSize);
    }
}
