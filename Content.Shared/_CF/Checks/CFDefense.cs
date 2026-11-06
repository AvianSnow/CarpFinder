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
            var trained = CFAttributeMath.IsSaveTrained(cls, saveName);
            bonus = CFAttributeMath.ComputeSave(abilities.AbilityModifiers, ability, combat.Level, trained);
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

        if (combat == null || abilities == null || string.IsNullOrEmpty(combat.ClassId)
            || !prototypes.TryIndex(combat.ClassId, out CFClassPrototype? cls))
        {
            return CFCommonerStats.Will + status - Math.Max(0, frightened);
        }

        var rank = CFSkills.GetPerceptionRank(cls);
        var bonus = abilities.GetModifier(CFAbility.Wisdom) + CFSkills.ProficiencyBonus(combat.Level, rank);
        return bonus + status - Math.Max(0, frightened);
    }

    /// <summary>Proficiency rank in <paramref name="skill"/>, from class, background and trained picks.</summary>
    public static CFProficiencyRank SkillRank(IPrototypeManager prototypes, CFCombatantComponent combat, string skill)
    {
        CFClassPrototype? cls = null;
        if (!string.IsNullOrEmpty(combat.ClassId))
            prototypes.TryIndex(combat.ClassId, out cls);

        CFBackgroundPrototype? background = null;
        if (!string.IsNullOrEmpty(combat.BackgroundId))
            prototypes.TryIndex(combat.BackgroundId, out background);

        var rank = CFSkills.GetSkillRank(cls, background, skill, combat.TrainedSkills);
        return CFSkills.RaiseRank(rank, combat.SkillIncreases.Count(id => id == skill));
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
