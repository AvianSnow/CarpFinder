using System.Linq;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.CF.Checks;

/// <summary>
/// Skill checks and defense DCs shared by activities, Strikes, and spells.
/// </summary>
public static class CFDefense
{
    /// <summary>The id for class DC in <see cref="CFCombatantComponent.ProficiencyRanks"/>.</summary>
    public const string ClassDcProficiency = "ClassDC";

    /// <summary>The id for spell attacks and spell DCs in <see cref="CFCombatantComponent.ProficiencyRanks"/>.</summary>
    public const string SpellProficiency = "Spellcasting";

    /// <summary>
    /// Saving throw modifier for <paramref name="defense"/>. Perception is routed to
    /// <see cref="PerceptionModifier"/>; any non-save defense falls back to Fortitude.
    /// </summary>
    public static int SaveModifier(
        IPrototypeManager prototypes,
        CFCombatantComponent? combat,
        CFAbilityScoreComponent? abilities,
        CFDefenseType defense,
        int status = 0,
        int frightened = 0)
    {
        if (defense == CFDefenseType.Perception)
            return PerceptionModifier(prototypes, combat, abilities, status, frightened);

        var printed = defense switch
        {
            CFDefenseType.Reflex => combat?.PrintedReflex,
            CFDefenseType.Will => combat?.PrintedWill,
            _ => combat?.PrintedFortitude,
        };
        if (printed is { } save)
            return save + status - Math.Max(0, frightened);

        var bonus = defense switch
        {
            CFDefenseType.Reflex => CFCommonerStats.Reflex,
            CFDefenseType.Will => CFCommonerStats.Will,
            _ => CFCommonerStats.Fortitude,
        };

        if (combat != null
            && abilities != null
            && !string.IsNullOrEmpty(combat.ClassId)
            && prototypes.TryIndex(combat.ClassId, out CFClassPrototype? cls))
        {
            var (ability, saveName) = defense switch
            {
                CFDefenseType.Reflex => (CFAbility.Dexterity, "Reflex"),
                CFDefenseType.Will => (CFAbility.Wisdom, "Will"),
                _ => (CFAbility.Constitution, "Fortitude"),
            };
            bonus = CFAttributeMath.ComputeSave(abilities.AbilityModifiers, ability, combat.Level, SaveRank(prototypes, combat, saveName));
        }

        return bonus + status - Math.Max(0, frightened);
    }

    public static int PerceptionModifier(
        IPrototypeManager prototypes,
        CFCombatantComponent? combat,
        CFAbilityScoreComponent? abilities,
        int status = 0,
        int frightened = 0)
    {
        if (combat?.PrintedPerception is { } printed)
            return printed + status - Math.Max(0, frightened);

        if (combat == null || abilities == null || ClassOf(prototypes, combat) == null)
            return CFCommonerStats.Will + status - Math.Max(0, frightened);

        var rank = PerceptionRank(prototypes, combat);
        var bonus = abilities.GetModifier(CFAbility.Wisdom) + CFSkills.ProficiencyBonus(combat.Level, rank);
        return bonus + status - Math.Max(0, frightened);
    }

    private static CFClassPrototype? ClassOf(IPrototypeManager prototypes, CFCombatantComponent? combat)
    {
        return !string.IsNullOrEmpty(combat?.ClassId) && prototypes.TryIndex(combat.ClassId, out CFClassPrototype? cls)
            ? cls
            : null;
    }

    /// <summary>The better of a rank from the class and one raised by a feat or class feature.</summary>
    public static CFProficiencyRank Best(CFCombatantComponent? combat, string proficiency, CFProficiencyRank rank)
    {
        var granted = combat?.ProficiencyRanks.GetValueOrDefault(proficiency) ?? CFProficiencyRank.Untrained;
        return granted > rank ? granted : rank;
    }

    public static CFProficiencyRank SaveRank(IPrototypeManager prototypes, CFCombatantComponent? combat, string saveName)
    {
        return Best(combat, saveName, CFSkills.GetSaveRank(ClassOf(prototypes, combat), saveName));
    }

    public static CFProficiencyRank PerceptionRank(IPrototypeManager prototypes, CFCombatantComponent? combat)
    {
        return Best(combat, "Perception", CFSkills.GetPerceptionRank(ClassOf(prototypes, combat)));
    }

    /// <summary>Rank with a weapon category. A creature with no class counts as trained with everything.</summary>
    public static CFProficiencyRank AttackRank(IPrototypeManager prototypes, CFCombatantComponent? combat, string proficiency)
    {
        var cls = ClassOf(prototypes, combat);
        return Best(combat, proficiency, cls == null ? CFProficiencyRank.Trained : CFSkills.GetAttackRank(cls, proficiency));
    }

    public static CFProficiencyRank ArmorRank(IPrototypeManager prototypes, CFCombatantComponent? combat, string proficiency)
    {
        return Best(combat, proficiency, CFSkills.GetDefenseRank(ClassOf(prototypes, combat), proficiency));
    }

    /// <summary>Everyone starts trained in their class DC and in casting.</summary>
    public static CFProficiencyRank ClassDcRank(CFCombatantComponent? combat)
    {
        return Best(combat, ClassDcProficiency, CFProficiencyRank.Trained);
    }

    public static CFProficiencyRank SpellRank(CFCombatantComponent? combat)
    {
        return Best(combat, SpellProficiency, CFProficiencyRank.Trained);
    }

    /// <summary>Proficiency rank in <paramref name="skill"/>, from class, background, trained picks and raised ranks.</summary>
    public static CFProficiencyRank SkillRank(IPrototypeManager prototypes, CFCombatantComponent combat, string skill)
    {
        var cls = ClassOf(prototypes, combat);

        CFBackgroundPrototype? background = null;
        if (!string.IsNullOrEmpty(combat.BackgroundId))
            prototypes.TryIndex(combat.BackgroundId, out background);

        var rank = CFSkills.GetSkillRank(cls, background, skill, combat.TrainedSkills);
        return Best(combat, skill, CFSkills.RaiseRank(rank, combat.SkillIncreases.Count(id => id == skill)));
    }

    public static int SkillModifier(
        IPrototypeManager prototypes,
        CFCombatantComponent combat,
        CFAbilityScoreComponent abilities,
        string skill,
        CFAbility ability,
        int circumstance = 0,
        int frightened = 0)
    {
        int mod;
        if (combat.PrintedSkills.TryGetValue(skill, out var printed))
            mod = printed + circumstance;
        else
            mod = CFSkills.SkillModifier(abilities.AbilityModifiers, ability, combat.Level, SkillRank(prototypes, combat, skill), circumstance);

        return mod - Math.Max(0, frightened);
    }

    public static CFDegree Roll(IRobustRandom random, int modifier, int dc)
    {
        return CFDegrees.DegreeOfSuccess(random.Next(1, 21), modifier, dc);
    }
}
