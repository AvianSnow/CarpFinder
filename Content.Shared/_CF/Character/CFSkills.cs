using System.Linq;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

/// <summary>PF2e proficiency ranks and the flat bonus they add (before level).</summary>
public enum CFProficiencyRank : byte
{
    Untrained = 0,
    Trained = 2,
    Expert = 4,
    Master = 6,
    Legendary = 8,
}

/// <summary>Skill / Perception check math for Pathfinder character sheets.</summary>
public static class CFSkills
{
    /// <summary>
    /// Sheet skill columns (user layout). Thievery is appended to column 3.
    /// </summary>
    public static readonly string[] Column1 =
    [
        "Acrobatics",
        "Crafting",
        "Intimidation",
        "Occultism",
        "Society",
    ];

    public static readonly string[] Column2 =
    [
        "Arcana",
        "Deception",
        "Medicine",
        "Performance",
        "Survival",
    ];

    public static readonly string[] Column3 =
    [
        "Athletics",
        "Diplomacy",
        "Nature",
        "Religion",
        "Stealth",
        "Thievery",
    ];

    public static int ProficiencyBonus(int level, CFProficiencyRank rank)
    {
        if (rank == CFProficiencyRank.Untrained)
            return 0;

        return level + (int)rank;
    }

    public static CFProficiencyRank RankFromList(CFProficiencyList list, string id)
    {
        if (list.Legendary.Contains(id))
            return CFProficiencyRank.Legendary;
        if (list.Master.Contains(id))
            return CFProficiencyRank.Master;
        if (list.Expert.Contains(id))
            return CFProficiencyRank.Expert;
        if (list.Trained.Contains(id))
            return CFProficiencyRank.Trained;
        return CFProficiencyRank.Untrained;
    }

    public static CFProficiencyRank GetSkillRank(
        CFClassPrototype? cls,
        CFBackgroundPrototype? background,
        string skillId)
    {
        var rank = CFProficiencyRank.Untrained;
        if (cls != null)
            rank = RankFromList(cls.Proficiencies.Skills, skillId);

        if (rank == CFProficiencyRank.Untrained
            && background != null
            && background.TrainedSkills.Any(s => s.Id == skillId))
        {
            rank = CFProficiencyRank.Trained;
        }

        return rank;
    }

    public static CFProficiencyRank GetSaveRank(CFClassPrototype? cls, string saveName)
    {
        return cls == null
            ? CFProficiencyRank.Untrained
            : RankFromList(cls.Proficiencies.SavingThrows, saveName);
    }

    public static CFProficiencyRank GetPerceptionRank(CFClassPrototype? cls)
    {
        return cls == null
            ? CFProficiencyRank.Untrained
            : RankFromList(cls.Proficiencies.Perception, "Perception");
    }

    public static int SkillModifier(
        IReadOnlyDictionary<CFAbility, int> mods,
        CFAbility ability,
        int level,
        CFProficiencyRank rank,
        int itemBonus = 0)
    {
        return mods.GetValueOrDefault(ability) + ProficiencyBonus(level, rank) + itemBonus;
    }

    public static int AbilityScoreFromModifier(int modifier) => 10 + modifier * 2;

    public static string FormatSigned(int value) => value >= 0 ? $"+{value}" : value.ToString();
}
