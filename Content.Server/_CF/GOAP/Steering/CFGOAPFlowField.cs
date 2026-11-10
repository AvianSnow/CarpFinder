using System.Numerics;

namespace Content.Server.CF.GOAP.Steering;

/// <summary>
/// Arrows on the ground pointing the way to a goal. One creature builds it, and any number can read the square under
/// their feet, wherever they are, so nobody can fall behind it.
/// </summary>
public sealed class CFGOAPFlowField
{
    /// <summary>Which way to go from each tile, grid-local.</summary>
    public Dictionary<Vector2i, Vector2> Arrows = new();

    /// <summary>Where it leads, in world space.</summary>
    public Vector2 Goal;

    /// <summary>Whether the arrows lead away from <see cref="Goal"/> rather than to it.</summary>
    public bool Away;

    public TimeSpan NextBuild;

    /// <summary>
    /// The navigator builds it again on its next move, whatever the timing. Set when a creature gets through a door,
    /// because the way may look different from the far side.
    /// </summary>
    public bool Rebuild;

    /// <summary>Doors and windows given up on, and until when. On the field, so a whole group gives up together.</summary>
    public Dictionary<EntityUid, TimeSpan> GaveUp = new();

    public void Clear()
    {
        Arrows = new Dictionary<Vector2i, Vector2>();
        GaveUp = new Dictionary<EntityUid, TimeSpan>();
        Away = false;
        Rebuild = false;
        NextBuild = TimeSpan.Zero;
    }
}

/// <summary>Builds flow fields as plain code, so it tests without a game.</summary>
public static class CFGOAPFlowFieldBuilder
{
    /// <summary>The cost of a tile that cannot be crossed at all.</summary>
    public const int Blocked = -1;

    private const int StraightCost = 10;

    private const int DiagonalCost = 14;

    // Above 1, so a long run past the threat can beat a short one away from it. 1.2 is the usual choice for a safety map.
    private const int FleeScale = 12;
    private const int FleeScaleBase = 10;

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
        var cost = Remembered(tileCost);
        var costs = new Dictionary<Vector2i, int> { [goal] = 0 };
        Spread(costs, cost, reach * StraightCost, grow: true);
        return Arrows(costs, cost, keepFlat: false);
    }

    /// <summary>
    /// Arrows that lead away from a threat, as a Dijkstra safety map. Plain distance from the threat would run a creature
    /// into the nearest dead end. So the distances are flipped and scaled, which makes the far ends of the map the low
    /// ground, and spread again, which fills in the dead ends that only look far away. Following the arrows downhill
    /// goes round corners toward real space. A tile with nowhere better to go keeps a zero arrow, so a reader can tell
    /// "as safe as it gets" from "off the field".
    /// </summary>
    public static Dictionary<Vector2i, Vector2> BuildFlee(Vector2i threat, Func<Vector2i, int> tileCost, int reach)
    {
        var cost = Remembered(tileCost);
        var distances = new Dictionary<Vector2i, int> { [threat] = 0 };
        Spread(distances, cost, reach * StraightCost, grow: true);

        var safety = new Dictionary<Vector2i, int>(distances.Count);
        foreach (var (tile, distance) in distances)
        {
            safety[tile] = -distance * FleeScale / FleeScaleBase;
        }

        Spread(safety, cost, int.MaxValue, grow: false);
        return Arrows(safety, cost, keepFlat: true);
    }

    /// <summary>
    /// The cheapest way between two tiles, by the same rules as the arrows, with no limit on how far apart they are.
    /// <paramref name="limit"/> caps how many tiles it looks at. The route leaves out where it starts. The goal counts
    /// as open even if something stands on it, as it does for a field.
    /// </summary>
    public static bool TryFindRoute(Vector2i start, Vector2i goal, Func<Vector2i, int> tileCost, int limit, out List<Vector2i> route)
    {
        route = new List<Vector2i>();
        var cost = Remembered(tile => tile == goal ? 0 : tileCost(tile));
        var costs = new Dictionary<Vector2i, int> { [start] = 0 };
        var cameFrom = new Dictionary<Vector2i, Vector2i>();
        var open = new PriorityQueue<Vector2i, int>();
        open.Enqueue(start, Estimate(start, goal));

        var searched = 0;
        while (open.TryDequeue(out var tile, out var priority))
        {
            if (tile == goal)
            {
                for (var at = goal; at != start; at = cameFrom[at])
                {
                    route.Add(at);
                }

                route.Reverse();
                return true;
            }

            if (priority > costs[tile] + Estimate(tile, goal))
                continue;

            if (++searched > limit)
                return false;

            foreach (var step in Steps)
            {
                var next = tile + step;
                if (!CanStep(tile, step, cost))
                    continue;

                var total = costs[tile] + cost(next) + (step.X != 0 && step.Y != 0 ? DiagonalCost : StraightCost);
                if (costs.TryGetValue(next, out var known) && known <= total)
                    continue;

                costs[next] = total;
                cameFrom[next] = tile;
                open.Enqueue(next, total + Estimate(next, goal));
            }
        }

        return false;
    }

    /// <summary>The cost of the way with nothing in it, which no real way can beat.</summary>
    private static int Estimate(Vector2i from, Vector2i to)
    {
        var x = Math.Abs(from.X - to.X);
        var y = Math.Abs(from.Y - to.Y);
        return Math.Min(x, y) * DiagonalCost + Math.Abs(x - y) * StraightCost;
    }

    /// <summary>The tile an arrow on <paramref name="tile"/> leads to.</summary>
    public static Vector2i NextTile(Vector2i tile, Vector2 arrow)
    {
        return tile + new Vector2i((int) MathF.Round(arrow.X), (int) MathF.Round(arrow.Y));
    }

    private static Func<Vector2i, int> Remembered(Func<Vector2i, int> tileCost)
    {
        var known = new Dictionary<Vector2i, int>();
        return tile =>
        {
            if (!known.TryGetValue(tile, out var extra))
            {
                extra = tileCost(tile);
                known[tile] = extra;
            }

            return extra;
        };
    }

    /// <summary>
    /// Lowers each tile to the cheapest way to it from any tile already in <paramref name="costs"/>.
    /// <paramref name="grow"/> lets it take in new tiles, up to <paramref name="limit"/>.
    /// </summary>
    private static void Spread(Dictionary<Vector2i, int> costs, Func<Vector2i, int> cost, int limit, bool grow)
    {
        var open = new PriorityQueue<Vector2i, int>();
        foreach (var (tile, value) in costs)
        {
            open.Enqueue(tile, value);
        }

        while (open.TryDequeue(out var tile, out var current))
        {
            if (current != costs[tile] || current >= limit)
                continue;

            foreach (var step in Steps)
            {
                var next = tile + step;
                if (!grow && !costs.ContainsKey(next) || !CanStep(tile, step, cost))
                    continue;

                var total = current + cost(next) + (step.X != 0 && step.Y != 0 ? DiagonalCost : StraightCost);
                if (costs.TryGetValue(next, out var known) && known <= total)
                    continue;

                costs[next] = total;
                open.Enqueue(next, total);
            }
        }
    }

    /// <summary>Points each tile at its lowest neighbour. <paramref name="keepFlat"/> keeps tiles with none, as zero.</summary>
    private static Dictionary<Vector2i, Vector2> Arrows(Dictionary<Vector2i, int> costs, Func<Vector2i, int> cost, bool keepFlat)
    {
        var arrows = new Dictionary<Vector2i, Vector2>(costs.Count);
        foreach (var (tile, value) in costs)
        {
            var best = value;
            var arrow = Vector2i.Zero;
            foreach (var step in Steps)
            {
                if (costs.TryGetValue(tile + step, out var neighbour) && neighbour < best && CanStep(tile, step, cost))
                {
                    best = neighbour;
                    arrow = step;
                }
            }

            if (arrow != Vector2i.Zero)
                arrows[tile] = Vector2.Normalize(arrow);
            else if (keepFlat)
                arrows[tile] = Vector2.Zero;
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
