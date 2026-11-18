namespace Content.Shared.CF.Terrain;

/// <summary>A Ranger's favored terrain, picked through a <c>Terrain</c> choice.</summary>
public enum CFFavoredTerrain : byte
{
    Aquatic,
    Arctic,
    Desert,
    Forest,
    Mountain,
    Plains,
    Sky,
    Swamp,
    Underground,
}

public static class CFFavoredTerrains
{
    /// <summary>
    /// The causes of difficult terrain found in each terrain. Favored Terrain ignores non-magical difficult terrain while
    /// in that terrain. The game has no map of terrains, so here it ignores these causes anywhere.
    /// </summary>
    public static CFTerrainCause[] Causes(CFFavoredTerrain terrain)
    {
        return terrain switch
        {
            CFFavoredTerrain.Aquatic => new[] { CFTerrainCause.Water },
            CFFavoredTerrain.Arctic => new[] { CFTerrainCause.Snow },
            CFFavoredTerrain.Desert => new[] { CFTerrainCause.Sand },
            CFFavoredTerrain.Forest => new[] { CFTerrainCause.Plants },
            CFFavoredTerrain.Mountain => new[] { CFTerrainCause.Stone, CFTerrainCause.Snow },
            CFFavoredTerrain.Plains => new[] { CFTerrainCause.Plants },
            CFFavoredTerrain.Swamp => new[] { CFTerrainCause.Water, CFTerrainCause.Plants },
            CFFavoredTerrain.Underground => new[] { CFTerrainCause.Stone, CFTerrainCause.Tight },
            _ => Array.Empty<CFTerrainCause>(),
        };
    }
}
