using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Checks;

/// <summary>Attack bonus, DC, and ability modifier for one kind of check a creature imposes.</summary>
public readonly record struct CFCastingStats(int AttackBonus, int Dc, int AbilityModifier)
{
    /// <summary>A creature with no sheet: +0 to attack, DC 10.</summary>
    public static readonly CFCastingStats Untrained = new(0, 10, 0);
}

public static class CFCasting
{
    /// <summary>
    /// Spell attack and spell DC. The class's spellcasting ability, or the best mental ability for a
    /// creature without a casting class. Trained proficiency until classes track spell proficiency ranks.
    /// </summary>
    public static CFCastingStats Spellcasting(
        IPrototypeManager prototypes,
        CFCombatantComponent? combat,
        CFAbilityScoreComponent? abilities)
    {
        if (combat == null || abilities == null)
            return CFCastingStats.Untrained;

        var ability = CFAbility.Charisma;
        if (combat.ClassId != null
            && prototypes.TryIndex(combat.ClassId, out CFClassPrototype? cls)
            && cls.Spellcasting != null)
        {
            ability = cls.Spellcasting.Ability;
        }
        else
        {
            var best = int.MinValue;
            foreach (var mental in new[] { CFAbility.Intelligence, CFAbility.Wisdom, CFAbility.Charisma })
            {
                var mod = abilities.GetModifier(mental);
                if (mod <= best)
                    continue;

                best = mod;
                ability = mental;
            }
        }

        return FromAbility(combat.Level, abilities.GetModifier(ability));
    }

    /// <summary>Class DC: 10 + key ability modifier + proficiency.</summary>
    public static CFCastingStats ClassDc(
        IPrototypeManager prototypes,
        CFCombatantComponent? combat,
        CFAbilityScoreComponent? abilities)
    {
        if (combat == null || abilities == null)
            return CFCastingStats.Untrained;

        var ability = CFAbility.Strength;
        if (combat.ClassId != null && prototypes.TryIndex(combat.ClassId, out CFClassPrototype? cls))
            ability = cls.KeyAbility;

        return FromAbility(combat.Level, abilities.GetModifier(ability));
    }

    /// <summary>A fixed DC (hazards, items). The matching attack bonus is DC − 10.</summary>
    public static CFCastingStats Fixed(int dc)
    {
        return new CFCastingStats(dc - 10, dc, 0);
    }

    /// <summary>Cantrips heighten to half the caster's level, rounded up, and never below 1.</summary>
    public static int CantripRank(int level)
    {
        return Math.Max(1, (level + 1) / 2);
    }

    private static CFCastingStats FromAbility(int level, int abilityMod)
    {
        var proficiency = CFAttributeMath.ProficiencyBonus(level, trained: true);
        return new CFCastingStats(abilityMod + proficiency, 10 + abilityMod + proficiency, abilityMod);
    }
}
