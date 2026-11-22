// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

namespace Content.Client.CF.ZLevels.TileVolume;

/// <summary>
/// Client-only render cache for <see cref="CFTileVolumeOverlay"/>, added lazily to grids the overlay draws.
/// Holds per-chunk bitmasks of tile edges that border an opening, one mask per grid-local direction.
/// </summary>
[RegisterComponent]
public sealed partial class CFTileVolumeCacheComponent : Component
{
    public readonly Dictionary<Vector2i, CFTileVolumeChunk> Chunks = new();
}

public sealed class CFTileVolumeChunk
{
    /// <summary>
    /// Side length of a cached chunk, in tiles. 8x8 tiles fit exactly into one <see cref="ulong"/> mask.
    /// </summary>
    public const int Size = 8;

    /// <summary>
    /// Shared instance for chunks without a single lip, so empty space costs no allocations.
    /// </summary>
    public static readonly CFTileVolumeChunk Empty = new(null);

    /// <summary>
    /// Indexed by <see cref="CFTileVolumeDirection"/>. Bit <c>y * Size + x</c> is set when the solid tile at
    /// local (x, y) borders an opening in that direction.
    /// </summary>
    public readonly ulong[] Masks = new ulong[CFTileVolumeDirection.Count];

    /// <summary>
    /// Tile type id per bit, only meaningful for bits set in any mask. Null for <see cref="Empty"/>.
    /// </summary>
    public readonly int[]? TileIds;

    public CFTileVolumeChunk(int[]? tileIds)
    {
        TileIds = tileIds;
    }
}

/// <summary>
/// Grid-local cardinal directions used by the tile volume cache. The lip texture is authored for
/// <see cref="South"/> and rotated by <see cref="Angles"/> for the others.
/// </summary>
public static class CFTileVolumeDirection
{
    public const int South = 0;
    public const int East = 1;
    public const int North = 2;
    public const int West = 3;
    public const int Count = 4;

    public static readonly Vector2i[] Offsets =
    [
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0),
    ];

    public static readonly Angle[] Angles =
    [
        Angle.Zero,
        Angle.FromDegrees(90),
        Angle.FromDegrees(180),
        Angle.FromDegrees(270),
    ];

    /// <summary>
    /// Snaps a grid-local vector to the nearest cardinal direction.
    /// </summary>
    public static int FromVector(System.Numerics.Vector2 vec)
    {
        if (MathF.Abs(vec.X) > MathF.Abs(vec.Y))
            return vec.X > 0 ? East : West;

        return vec.Y < 0 ? South : North;
    }
}
