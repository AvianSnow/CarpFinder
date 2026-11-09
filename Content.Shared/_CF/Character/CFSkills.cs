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

        if (cls != null)
        {
            foreach (var bonusLevel in cls.BonusSkillIncreases)
            {
                if (bonusLevel <= level && build.BonusSkillIncreases.GetValueOrDefault(bonusLevel) == skillId)
                    increases++;
            }
        }

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
            foreach (var id in BackgroundFeats(prototypes, build))
            {
                AddFeat(prototypes, result, id, _ => true);
            }

            AddFeat(prototypes, result, build.AncestryFeat, feat => feat.Fits(ancestryId, 1));
            AddFeat(prototypes, result, build.ClassFeat, feat => feat.FitsClass(classId, 1) && CFClassChoices.MeetsRequirement(prototypes, build, feat));
        }

        if (level >= 2)
        {
            AddFeat(prototypes, result, build.ClassFeatLevel2, feat => feat.FitsClass(classId, 2) && CFClassChoices.MeetsRequirement(prototypes, build, feat));
            AddFeat(prototypes, result, build.SkillFeatLevel2, feat => feat.FitsSkillSlot(2));
        }

        if (level >= 3)
            AddFeat(prototypes, result, build.GeneralFeatLevel3, feat => feat.FitsGeneralSlot(3));

        if (level >= 4)
        {
            AddFeat(prototypes, result, build.ClassFeatLevel4, feat => feat.FitsClass(classId, 4) && CFClassChoices.MeetsRequirement(prototypes, build, feat));
            AddFeat(prototypes, result, build.SkillFeatLevel4, feat => feat.FitsSkillSlot(4));
        }

        if (level >= 5)
            AddFeat(prototypes, result, build.AncestryFeatLevel5, feat => feat.Fits(ancestryId, 5));

        if (classId != null && prototypes.TryIndex<CFClassPrototype>(classId, out var cls))
        {
            foreach (var bonusLevel in cls.BonusSkillFeats)
            {
                if (bonusLevel <= level && build.BonusSkillFeats.TryGetValue(bonusLevel, out var id))
                    AddFeat(prototypes, result, new ProtoId<CFFeatPrototype>(id), feat => feat.FitsSkillSlot(bonusLevel));
            }
        }

        return result;
    }

    /// <summary>The feats the build's background gives.</summary>
    public static IReadOnlyList<ProtoId<CFFeatPrototype>> BackgroundFeats(IPrototypeManager prototypes, CFCharacterBuildData build)
    {
        return build.Background is { } id && prototypes.TryIndex(id, out CFBackgroundPrototype? background)
            ? background.SkillFeats
            : Array.Empty<ProtoId<CFFeatPrototype>>();
    }

    /// <summary>
    /// Clears the skill and general feat slots the build no longer qualifies for, and extra slots its class does not have.
    /// Dropping a feat can cost a later one the skill it trained, so this repeats until nothing changes.
    /// </summary>
    /// <returns>Whether anything was cleared.</returns>
    public static bool DropUnqualifiedFeats(IPrototypeManager prototypes, CFCharacterBuildData build, string? ancestryId)
    {
        CFClassPrototype? cls = null;
        if (build.Class is { } classId)
            prototypes.TryIndex(classId, out cls);

        var dropped = false;
        foreach (var level in build.BonusSkillIncreases.Keys.ToList())
        {
            if (cls?.BonusSkillIncreases.Contains(level) == true)
                continue;

            build.BonusSkillIncreases.Remove(level);
            dropped = true;
        }

        var given = BackgroundFeats(prototypes, build).Select(id => id.Id).ToHashSet();
        bool Bad(string? id, int level)
        {
            return id != null
                   && (given.Contains(id)
                       || !prototypes.TryIndex<CFFeatPrototype>(id, out var feat)
                       || !Qualifies(prototypes, build, ancestryId, feat, level));
        }

        bool again;
        do
        {
            again = false;
            if (Bad(build.SkillFeatLevel2?.Id, 2))
            {
                build.SkillFeatLevel2 = null;
                again = true;
            }

            if (Bad(build.GeneralFeatLevel3?.Id, 3))
            {
                build.GeneralFeatLevel3 = null;
                again = true;
            }

            if (Bad(build.SkillFeatLevel4?.Id, 4))
            {
                build.SkillFeatLevel4 = null;
                again = true;
            }

            foreach (var (level, id) in build.BonusSkillFeats.ToList())
            {
                if (cls?.BonusSkillFeats.Contains(level) == true && !Bad(id, level))
                    continue;

                build.BonusSkillFeats.Remove(level);
                again = true;
            }

            dropped |= again;
        } while (again);

        return dropped;
    }

    private static void AddFeat(
        IPrototypeManager prototypes,
        List<CFFeatPrototype> result,
        ProtoId<CFFeatPrototype>? id,
        Func<CFFeatPrototype, bool> fits)
    {
        if (id != null
            && prototypes.TryIndex(id.Value, out CFFeatPrototype? feat)
            && fits(feat)
            && !result.Exists(other => other.ID == feat.ID))
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

        result.AddRange(CFClassChoices.TrainedSkills(prototypes, build));
        result.AddRange(HeritageSkills(prototypes, build, ancestryId));
        result.AddRange(CFOptionChoices.TrainedSkills(prototypes, build, ancestryId, level));

        foreach (var feat in SelectedFeats(prototypes, build, ancestryId, classId, level))
        {
            foreach (var skill in feat.TrainedSkills)
                result.Add(skill.Id);
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Skills trained by the build's heritage, if it fits the ancestry.</summary>
    public static List<string> HeritageSkills(IPrototypeManager prototypes, CFCharacterBuildData build, string? ancestryId)
    {
        var result = new List<string>();
        if (build.Heritage is not { } id
            || !prototypes.TryIndex(id, out CFHeritagePrototype? heritage)
            || !heritage.Fits(ancestryId))
        {
            return result;
        }

        foreach (var skill in heritage.TrainedSkills)
            result.Add(skill.Id);

        return result;
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

    /// <summary>A class's rank with a weapon category, by its name in <c>attacks:</c>.</summary>
    public static CFProficiencyRank GetAttackRank(CFClassPrototype? cls, string proficiency)
    {
        return cls == null
            ? CFProficiencyRank.Untrained
            : RankFromList(cls.Proficiencies.Attacks, proficiency);
    }

    /// <summary>A class's rank with armor or unarmored defense, by its name in <c>defenses:</c>.</summary>
    public static CFProficiencyRank GetDefenseRank(CFClassPrototype? cls, string proficiency)
    {
        return cls == null
            ? CFProficiencyRank.Untrained
            : RankFromList(cls.Proficiencies.Defenses, proficiency);
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
