using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Skill checks and defense DCs shared by activities and spells.
/// </summary>
public static class CFDefense
{
    public static int SaveModifier(
        IPrototypeManager prototypes,
        CFCombatantComponent? combat,
        CFAbilityScoreComponent? abilities,
        CFSpellDefense defense,
        int status = 0,
        int frightened = 0)
    {
        var bonus = defense switch
        {
            CFSpellDefense.Reflex => CFCommonerStats.Reflex,
            CFSpellDefense.Will => CFCommonerStats.Will,
            _ => CFCommonerStats.Fortitude,
        };

        if (combat != null
            && abilities != null
            && !string.IsNullOrEmpty(combat.ClassId)
            && prototypes.TryIndex(combat.ClassId, out CFClassPrototype? cls))
        {
            var ability = defense switch
            {
                CFSpellDefense.Reflex => CFAbility.Dexterity,
                CFSpellDefense.Will => CFAbility.Wisdom,
                _ => CFAbility.Constitution,
            };
            var trained = CFAttributeMath.IsSaveTrained(cls, defense.ToString());
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
        if (combat == null || abilities == null || string.IsNullOrEmpty(combat.ClassId)
            || !prototypes.TryIndex(combat.ClassId, out CFClassPrototype? cls))
        {
            return CFCommonerStats.Will + status - Math.Max(0, frightened);
        }

        var rank = CFSkills.GetPerceptionRank(cls);
        var bonus = abilities.GetModifier(CFAbility.Wisdom) + CFSkills.ProficiencyBonus(combat.Level, rank);
        return bonus + status - Math.Max(0, frightened);
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
        CFClassPrototype? cls = null;
        if (!string.IsNullOrEmpty(combat.ClassId))
            prototypes.TryIndex(combat.ClassId, out cls);

        CFBackgroundPrototype? background = null;
        if (!string.IsNullOrEmpty(combat.BackgroundId))
            prototypes.TryIndex(combat.BackgroundId, out background);

        var rank = CFSkills.GetSkillRank(cls, background, skill, combat.TrainedSkills);
        var mod = CFSkills.SkillModifier(abilities.AbilityModifiers, ability, combat.Level, rank, circumstance);
        return mod - Math.Max(0, frightened);
    }

    public static CFStrikeDegree Roll(IRobustRandom random, int modifier, int dc)
    {
        return CFMelee.DegreeOfSuccess(random.Next(1, 21), modifier, dc);
    }
}
