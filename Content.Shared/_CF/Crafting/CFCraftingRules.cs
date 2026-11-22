using Content.Shared.CF.Character;

namespace Content.Shared.CF.Crafting;

/// <summary>
/// Numbers from the remaster Craft activity. Everything measured in days is left out on purpose:
/// a craft takes the recipe's few seconds at a workbench.
/// </summary>
public static class CFCraftingRules
{
    public const string Skill = "Crafting";

    /// <summary>DCs by level, 0 to 25 (GM Core).</summary>
    private static readonly int[] LevelDcs =
    {
        14, 15, 16, 18, 19, 20, 22, 23, 24, 26, 27, 28, 30,
        31, 32, 34, 35, 36, 38, 39, 40, 42, 44, 46, 48, 50,
    };

    public static int LevelDc(int level)
    {
        // The book's table starts at level -1, which only creatures can be.
        if (level < 0)
            return 13;

        return LevelDcs[Math.Min(level, LevelDcs.Length - 1)];
    }

    /// <summary>Craft DC for an item: its level, made harder by rarity.</summary>
    public static int Dc(int level, CFRarity rarity)
    {
        var adjustment = rarity switch
        {
            CFRarity.Uncommon => 2,
            CFRarity.Rare => 5,
            _ => 0,
        };
        return LevelDc(level) + adjustment;
    }

    /// <summary>Crafting rank an item of this level needs: master from 9, legendary from 17.</summary>
    public static CFProficiencyRank RequiredRank(int level)
    {
        if (level >= 17)
            return CFProficiencyRank.Legendary;

        return level >= 9 ? CFProficiencyRank.Master : CFProficiencyRank.Trained;
    }

    /// <summary>A critical failure ruins 10% of the raw materials, rounded up.</summary>
    public static int RuinedUnits(int totalUnits)
    {
        return (totalUnits + 9) / 10;
    }
}
