using Content.Shared.CF.Checks;

namespace Content.Shared.CF.Harvest;

/// <summary>
/// What a monster's parts are worth. Values are Battlezoo Bestiary's monster parts by creature level, the Light
/// column (Table 2A), because CarpFinder still hands out coin as treasure: parts come on top of it.
/// </summary>
public static class CFHarvestRules
{
    public const int LowestLevel = -1;

    /// <summary>Copper per monster, from level -1 to 25.</summary>
    private static readonly int[] LightValues =
    {
        150, 225, 350, 500, 700, 1_200, 1_800, 3_000, 4_500, 6_400,
        9_000, 12_500, 17_500, 25_000, 37_500, 56_000, 81_000, 125_000, 187_500, 300_000,
        500_000, 875_000, 1_000_000, 1_750_000, 2_000_000, 3_500_000, 4_000_000,
    };

    /// <summary>The parts of one monster of this level at full worth, in copper.</summary>
    public static int Value(int level)
    {
        var index = Math.Clamp(level - LowestLevel, 0, LightValues.Length - 1);
        return LightValues[index];
    }

    /// <summary>
    /// The percent of the value a harvest keeps: a critical failure ruins the parts, a failure keeps half, a success
    /// all, and a critical success half again.
    /// </summary>
    public static int Percent(CFDegree degree)
    {
        return degree switch
        {
            CFDegree.CriticalSuccess => 150,
            CFDegree.Success => 100,
            CFDegree.Failure => 50,
            _ => 0,
        };
    }

    /// <summary>What a harvest of a monster of this level yields at this degree, in copper.</summary>
    public static int Yield(int level, CFDegree degree)
    {
        return Value(level) * Percent(degree) / 100;
    }
}
