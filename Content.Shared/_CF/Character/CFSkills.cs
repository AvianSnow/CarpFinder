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

/// <summary>Skill / Perception check math for Carpfinder character sheets.</summary>
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
        "Lore",
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
        string skillId,
        IReadOnlyCollection<string>? trainedSkills = null)
    {
        var rank = CFProficiencyRank.Untrained;
        if (cls != null)
            rank = RankFromList(cls.Proficiencies.Skills, skillId);

        if (rank != CFProficiencyRank.Untrained)
            return rank;

        if (trainedSkills != null)
            return trainedSkills.Contains(skillId) ? CFProficiencyRank.Trained : CFProficiencyRank.Untrained;

        // Fallback when no explicit picks (NPC / incomplete build): prototype background lists.
        if (background != null
            && (background.TrainedSkills.Any(s => s.Id == skillId)
                || background.SkillBoosts.Any(s => s.Id == skillId)))
        {
            return CFProficiencyRank.Trained;
        }

        return CFProficiencyRank.Untrained;
    }

    /// <summary>
    /// Background + free class skill picks for a build (class fixed skills stay on the class list).
    /// </summary>
    /// <summary>
    /// A skill increase raises a rank by one. Master needs level 7, so nothing below that goes past Expert.
    /// </summary>
    public static CFProficiencyRank RaiseRank(CFProficiencyRank rank, int increases)
    {
        for (var i = 0; i < increases; i++)
        {
            if (rank == CFProficiencyRank.Untrained)
                rank = CFProficiencyRank.Trained;
            else if (rank == CFProficiencyRank.Trained)
                rank = CFProficiencyRank.Expert;
        }

        return rank;
    }

    /// <summary>The rank this build will have in a skill at a level, counting the skill increases by then.</summary>
    public static CFProficiencyRank BuildRank(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        string skillId,
        int level)
    {
        CFClassPrototype? cls = null;
        if (build.Class is { } classId)
            prototypes.TryIndex(classId, out cls);

        CFBackgroundPrototype? background = null;
        if (build.Background is { } backgroundId)
            prototypes.TryIndex(backgroundId, out background);

        var trained = CollectTrainedSkills(prototypes, build, ancestryId, cls?.ID, level);
        var increases = 0;
        if (level >= 3 && build.SkillIncreaseLevel3?.Id == skillId)
            increases++;
        if (level >= 5 && build.SkillIncreaseLevel5?.Id == skillId)
            increases++;

        return RaiseRank(GetSkillRank(cls, background, skillId, trained), increases);
    }

    /// <summary>Whether this build meets a feat's skill requirement at a level.</summary>
    public static bool Qualifies(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        CFFeatPrototype feat,
        int level)
    {
        return feat.RequiresSkill is not { } skill
               || BuildRank(prototypes, build, ancestryId, skill.Id, level) >= feat.RequiresRank;
    }

    public static List<CFFeatPrototype> SelectedFeats(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        string? classId,
        int level)
    {
        var result = new List<CFFeatPrototype>();
        if (level >= 1)
        {
            AddFeat(prototypes, result, build.AncestryFeat, feat => feat.Fits(ancestryId, 1));
            AddFeat(prototypes, result, build.ClassFeat, feat => feat.FitsClass(classId, 1));
        }

        if (level >= 2)
        {
            AddFeat(prototypes, result, build.ClassFeatLevel2, feat => feat.FitsClass(classId, 2));
            AddFeat(prototypes, result, build.SkillFeatLevel2, feat => feat.FitsSkillSlot(2));
        }

        if (level >= 3)
            AddFeat(prototypes, result, build.GeneralFeatLevel3, feat => feat.FitsGeneralSlot(3));

        if (level >= 4)
        {
            AddFeat(prototypes, result, build.ClassFeatLevel4, feat => feat.FitsClass(classId, 4));
            AddFeat(prototypes, result, build.SkillFeatLevel4, feat => feat.FitsSkillSlot(4));
        }

        if (level >= 5)
            AddFeat(prototypes, result, build.AncestryFeatLevel5, feat => feat.Fits(ancestryId, 5));
        return result;
    }

    private static void AddFeat(
        IPrototypeManager prototypes,
        List<CFFeatPrototype> result,
        ProtoId<CFFeatPrototype>? id,
        Func<CFFeatPrototype, bool> fits)
    {
        if (id != null
            && prototypes.TryIndex(id.Value, out CFFeatPrototype? feat)
            && fits(feat))
            result.Add(feat);
    }

    public static List<string> CollectTrainedSkills(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId = null,
        string? classId = null,
        int level = 1)
    {
        var result = new List<string>();

        if (build.BackgroundTrainedSkills.Count > 0)
        {
            foreach (var skill in build.BackgroundTrainedSkills)
                result.Add(skill.Id);
        }
        else if (build.Background != null
                 && prototypes.TryIndex(build.Background.Value, out CFBackgroundPrototype? background))
        {
            foreach (var skill in BackgroundSkillSlots(background))
                result.Add(skill.Id);
        }

        foreach (var skill in build.ClassTrainedSkills)
            result.Add(skill.Id);

        foreach (var feat in SelectedFeats(prototypes, build, ancestryId, classId, level))
        {
            foreach (var skill in feat.TrainedSkills)
                result.Add(skill.Id);
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    public static List<ProtoId<CFAbilitySkillPrototype>> BackgroundSkillSlots(CFBackgroundPrototype background)
    {
        return background.SkillBoosts
            .Concat(background.TrainedSkills)
            .Distinct()
            .ToList();
    }

    public static int ClassSkillSlotCount(
        CFClassPrototype cls,
        IReadOnlyDictionary<CFAbility, int> abilityModifiers)
    {
        var intMod = Math.Max(0, abilityModifiers.GetValueOrDefault(CFAbility.Intelligence));
        return Math.Max(0, cls.AdditionalSkillTrainings + intMod);
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
