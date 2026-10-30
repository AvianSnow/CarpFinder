using System.Numerics;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Pathfinder 2e flanking for creatures that each occupy one tile.
/// A line between the centers of two allies must pass through opposite sides
/// or opposite corners of the foe's space.
/// </summary>
public static class CFFlanking
{
    /// <summary>Off-guard is a −2 circumstance penalty to AC.</summary>
    public const int OffGuardAcPenalty = 2;

    /// <summary>Normal melee reach. The reach trait is 10.</summary>
    public const int DefaultReachFeet = 5;

    /// <summary>Lookup radius (tiles) when searching for flankers. Covers a 30-foot reach.</summary>
    public const float SearchRange = 8f;

    public static int ReachInTiles(int reachFeet)
    {
        if (reachFeet <= 0)
            return 0;

        return Math.Max(1, reachFeet / (int)CFMelee.FeetPerTile);
    }

    /// <summary>
    /// Chebyshev distance on the 5-foot grid. Diagonals cost one square, matching PF2e.
    /// The foe's own tile does not count as within reach.
    /// </summary>
    public static bool InReach(Vector2i from, Vector2i target, int reachFeet)
    {
        var tiles = ReachInTiles(reachFeet);
        if (tiles <= 0)
            return false;

        var delta = from - target;
        var chebyshev = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
        return chebyshev >= 1 && chebyshev <= tiles;
    }

    /// <summary>
    /// SS14 melee <c>Range</c> that reaches the far corner of the reach square
    /// without also reaching the next orthogonal square.
    /// </summary>
    public static float Ss14SwingRange(int reachFeet)
    {
        return ReachInTiles(reachFeet) * MathF.Sqrt(2f) + 0.05f;
    }

    /// <summary>
    /// True when the segment between the centers of <paramref name="a"/> and <paramref name="b"/>
    /// passes through opposite sides or opposite corners of the 1-tile space at <paramref name="target"/>.
    /// </summary>
    public static bool CentersFlank(Vector2i a, Vector2i b, Vector2i target)
    {
        if (a == b || a == target || b == target)
            return false;

        var p = new Vector2(a.X - target.X, a.Y - target.Y);
        var q = new Vector2(b.X - target.X, b.Y - target.Y);
        const float half = 0.5f;

        var left = HitsVertical(p, q, -half, half);
        var right = HitsVertical(p, q, half, half);
        var bottom = HitsHorizontal(p, q, -half, half);
        var top = HitsHorizontal(p, q, half, half);
        return left && right || top && bottom;
    }

    private const float Epsilon = 0.0001f;

    private static bool HitsVertical(Vector2 p, Vector2 q, float x, float half)
    {
        var dx = q.X - p.X;
        if (MathF.Abs(dx) < Epsilon)
            return false;

        var t = (x - p.X) / dx;
        if (t < -Epsilon || t > 1f + Epsilon)
            return false;

        var y = p.Y + t * (q.Y - p.Y);
        return y >= -half - Epsilon && y <= half + Epsilon;
    }

    private static bool HitsHorizontal(Vector2 p, Vector2 q, float y, float half)
    {
        var dy = q.Y - p.Y;
        if (MathF.Abs(dy) < Epsilon)
            return false;

        var t = (y - p.Y) / dy;
        if (t < -Epsilon || t > 1f + Epsilon)
            return false;

        var x = p.X + t * (q.X - p.X);
        return x >= -half - Epsilon && x <= half + Epsilon;
    }
}
