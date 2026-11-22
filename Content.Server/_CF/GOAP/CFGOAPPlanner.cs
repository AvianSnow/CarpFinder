using System.Numerics;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP;

/// <summary>Facts that must hold (<see cref="Mask"/> says which, <see cref="Value"/> says how).</summary>
public readonly record struct CFGOAPConditions(ulong Mask, ulong Value)
{
    /// <summary>The facts in <see cref="Mask"/> that do not match the world.</summary>
    public ulong Unmet(ulong world)
    {
        return (world ^ Value) & Mask;
    }

    public static CFGOAPConditions From(IEnumerable<CFGOAPFact> wanted, IEnumerable<CFGOAPFact> unwanted)
    {
        ulong mask = 0;
        ulong value = 0;
        foreach (var fact in wanted)
        {
            mask |= (ulong) fact;
            value |= (ulong) fact;
        }

        foreach (var fact in unwanted)
        {
            mask |= (ulong) fact;
            value &= ~(ulong) fact;
        }

        return new CFGOAPConditions(mask, value);
    }
}

/// <summary>One thing the planner can choose. <see cref="Index"/> says which action it stands for.</summary>
public readonly record struct CFGOAPOption(int Index, CFGOAPConditions Needs, CFGOAPConditions Gives, float Cost);

/// <summary>
/// Backward A* from a goal to the world as it is. Each step replaces what is still wanted with what the chosen
/// action needs, so the plan comes out in the order it should run. Plain code, so it tests without a game.
/// </summary>
public static class CFGOAPPlanner
{
    public const int MaxDepth = 8;

    public const int MaxNodes = 512;

    private sealed record Node(CFGOAPConditions Wanted, float Cost, Node? Next, int Action, int Depth);

    /// <summary>The cheapest list of options that reaches the goal, or null if there is none within the limits.</summary>
    public static List<int>? Plan(ulong world, CFGOAPConditions goal, IReadOnlyList<CFGOAPOption> options)
    {
        // A missing fact costs at least the cheapest option. Costs go below 1, so counting each as a flat 1 could put
        // a cheap route behind a dearer one.
        var cheapest = float.MaxValue;
        foreach (var option in options)
        {
            cheapest = MathF.Min(cheapest, option.Cost);
        }

        var open = new PriorityQueue<Node, float>();
        open.Enqueue(new Node(goal, 0f, null, -1, 0), 0f);

        for (var expanded = 0; expanded < MaxNodes && open.TryDequeue(out var node, out _); expanded++)
        {
            var unmet = node.Wanted.Unmet(world);
            if (unmet == 0)
                return Unroll(node);

            if (node.Depth >= MaxDepth)
                continue;

            foreach (var option in options)
            {
                if (!TryRegress(node.Wanted, unmet, option, out var wanted))
                    continue;

                var cost = node.Cost + option.Cost;
                var next = new Node(wanted, cost, node, option.Index, node.Depth + 1);
                open.Enqueue(next, cost + BitOperations.PopCount(wanted.Unmet(world)) * cheapest);
            }
        }

        return null;
    }

    /// <summary>What must hold before this option, for everything wanted to hold after it.</summary>
    private static bool TryRegress(CFGOAPConditions wanted, ulong unmet, CFGOAPOption option, out CFGOAPConditions result)
    {
        result = default;

        // It has to give something still missing, and not undo anything else.
        var gives = option.Gives;
        var helps = gives.Mask & unmet & ~(gives.Value ^ wanted.Value);
        var harms = gives.Mask & wanted.Mask & (gives.Value ^ wanted.Value);
        if (helps == 0 || harms != 0)
            return false;

        // What it does not give must still hold before it, alongside what it needs.
        var kept = wanted.Mask & ~gives.Mask;
        if ((kept & option.Needs.Mask & (wanted.Value ^ option.Needs.Value)) != 0)
            return false;

        result = new CFGOAPConditions(kept | option.Needs.Mask, (wanted.Value & kept) | option.Needs.Value);
        return true;
    }

    private static List<int> Unroll(Node node)
    {
        var plan = new List<int>();
        for (var step = node; step.Next != null; step = step.Next)
        {
            plan.Add(step.Action);
        }

        return plan;
    }
}
