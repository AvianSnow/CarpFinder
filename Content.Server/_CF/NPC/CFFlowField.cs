using System.Numerics;

namespace Content.Server.CF.NPC;

/// <summary>
/// Arrows on the ground pointing the way to a goal. One creature builds it, and any number can read the square under
/// their feet, wherever they are, so nobody can fall behind it.
/// </summary>
public sealed class CFFlowField
{
    /// <summary>Which way to go from each tile, grid-local.</summary>
    public Dictionary<Vector2i, Vector2> Arrows = new();

    /// <summary>Where it leads, in world space.</summary>
    public Vector2 Goal;

    public TimeSpan NextBuild;

    /// <summary>Doors and windows given up on, and until when. On the field, so a whole group gives up together.</summary>
    public Dictionary<EntityUid, TimeSpan> GaveUp = new();

    public void Clear()
    {
        Arrows = new Dictionary<Vector2i, Vector2>();
        GaveUp = new Dictionary<EntityUid, TimeSpan>();
        NextBuild = TimeSpan.Zero;
    }
}

/// <summary>Builds flow fields as plain code, so it tests without a game.</summary>
public static class CFFlowFieldBuilder
{
    /// <summary>The cost of a tile that cannot be crossed at all.</summary>
    public const int Blocked = -1;

    private const int StraightCost = 10;

    private const int DiagonalCost = 14;

    private static readonly Vector2i[] Steps =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// <summary>
    /// Spreads out from the goal, then points each tile at its cheapest neighbour. It stops
    /// <paramref name="reach"/> tiles out, so the cost has a limit however big the map is.
    /// <paramref name="tileCost"/> is what crossing a tile adds, where 0 is open floor and a plain step costs 10.
    /// <see cref="Blocked"/> means it cannot be crossed.
    /// </summary>
    public static Dictionary<Vector2i, Vector2> Build(Vector2i goal, Func<Vector2i, int> tileCost, int reach)
    {
        var known = new Dictionary<Vector2i, int>();
        int Cost(Vector2i tile)
        {
            if (!known.TryGetValue(tile, out var extra))
            {
                extra = tileCost(tile);
                known[tile] = extra;
            }

            return extra;
        }

        var costs = new Dictionary<Vector2i, int> { [goal] = 0 };
        var open = new PriorityQueue<Vector2i, int>();
        open.Enqueue(goal, 0);
        var limit = reach * StraightCost;

        while (open.TryDequeue(out var tile, out var cost))
        {
            if (cost != costs[tile] || cost >= limit)
                continue;

            foreach (var step in Steps)
            {
                var next = tile + step;
                if (!CanStep(tile, step, Cost))
                    continue;

                var total = cost + Cost(next) + (step.X != 0 && step.Y != 0 ? DiagonalCost : StraightCost);
                if (costs.TryGetValue(next, out var known2) && known2 <= total)
                    continue;

                costs[next] = total;
                open.Enqueue(next, total);
            }
        }

        var arrows = new Dictionary<Vector2i, Vector2>(costs.Count);
        foreach (var (tile, cost) in costs)
        {
            if (tile == goal)
                continue;

            var best = cost;
            var arrow = Vector2i.Zero;
            foreach (var step in Steps)
            {
                if (costs.TryGetValue(tile + step, out var neighbour) && neighbour < best && CanStep(tile, step, Cost))
                {
                    best = neighbour;
                    arrow = step;
                }
            }

            if (arrow != Vector2i.Zero)
                arrows[tile] = Vector2.Normalize(arrow);
        }

        return arrows;
    }

    /// <summary>A diagonal step needs both tiles beside it open, or a creature would clip the corner.</summary>
    private static bool CanStep(Vector2i from, Vector2i step, Func<Vector2i, int> cost)
    {
        if (cost(from + step) == Blocked)
            return false;

        return step.X == 0 || step.Y == 0 || cost(from + new Vector2i(step.X, 0)) == 0 && cost(from + new Vector2i(0, step.Y)) == 0;
    }
}
