using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Robust.Shared.Random;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Pathfinder 2e melee Strike helpers (d20 vs AC, degrees of success, damage dice).
/// </summary>
public static class CFMelee
{
    /// <summary>
    /// Melee reach in tiles. One SS14 tile is ~1.8 m (~6 ft); 1.25 covers adjacent + a little reach.
    /// </summary>
    public const float MeleeRange = 1.25f;

    /// <summary>Default PC / commoner fist Strike (PF2e unarmed): 1d4 bludgeoning.</summary>
    public const int UnarmedDamageDice = 1;
    public const int UnarmedDamageDie = 4;
    public const string UnarmedDamageType = "bludgeoning";
    public const string UnarmedProficiency = "Unarmed";

    public static bool InReach(float distance) => distance <= MeleeRange;

    public static int AttackModifier(
        int strengthMod,
        int dexterityMod,
        int proficiencyBonus,
        bool finesse,
        int itemBonus = 0)
    {
        var ability = finesse ? Math.Max(strengthMod, dexterityMod) : strengthMod;
        return ability + proficiencyBonus + itemBonus;
    }

    public static int DamageModifier(int strengthMod, bool finesse, int dexterityMod)
    {
        // Finesse weapons still add Strength to damage unless they have a trait that says otherwise.
        // For MVP, finesse only affects the attack roll; damage always uses Strength.
        _ = finesse;
        _ = dexterityMod;
        return strengthMod;
    }

    public static CFStrikeDegree DegreeOfSuccess(int d20, int attackBonus, int targetAc)
    {
        var total = d20 + attackBonus;
        var degree = total >= targetAc ? CFStrikeDegree.Success : CFStrikeDegree.Failure;

        if (total >= targetAc + 10)
            degree = CFStrikeDegree.CriticalSuccess;
        else if (total <= targetAc - 10)
            degree = CFStrikeDegree.CriticalFailure;

        // Natural 20 / 1 adjust the degree by one step.
        if (d20 == 20)
            degree = Improve(degree);
        else if (d20 == 1)
            degree = Worsen(degree);

        return degree;
    }

    public static int RollDamage(IRobustRandom random, int diceCount, int dieFaces, int damageMod, bool critical)
    {
        var dice = critical ? diceCount * 2 : diceCount;
        var total = damageMod;
        for (var i = 0; i < dice; i++)
            total += random.Next(1, dieFaces + 1);

        return Math.Max(0, total);
    }

    public static int AttackBonusFromSheet(CFCharacterSheet sheet, CFMeleeWeaponComponent weapon)
    {
        var trained = CFAttributeMath.IsWeaponTrained(sheet.Class, weapon.Proficiency);
        var proficiency = CFAttributeMath.ProficiencyBonus(sheet.Level, trained);
        return AttackModifier(
            sheet.Modifiers[CFAbility.Strength],
            sheet.Modifiers[CFAbility.Dexterity],
            proficiency,
            weapon.Finesse,
            weapon.ItemBonus);
    }

    public static int AttackBonusFromCombatant(
        CFAbilityScoreComponent abilities,
        CFCombatantComponent combatant,
        CFMeleeWeaponComponent weapon,
        bool weaponTrained)
    {
        return AttackBonusFromCombatant(
            abilities,
            combatant,
            weapon.Finesse,
            weapon.ItemBonus,
            weaponTrained);
    }

    public static int AttackBonusFromCombatant(
        CFAbilityScoreComponent abilities,
        CFCombatantComponent combatant,
        bool finesse,
        int itemBonus,
        bool weaponTrained)
    {
        var proficiency = CFAttributeMath.ProficiencyBonus(combatant.Level, weaponTrained);
        return AttackModifier(
            abilities.GetModifier(CFAbility.Strength),
            abilities.GetModifier(CFAbility.Dexterity),
            proficiency,
            finesse,
            itemBonus);
    }

    private static CFStrikeDegree Improve(CFStrikeDegree degree)
    {
        return degree switch
        {
            CFStrikeDegree.CriticalFailure => CFStrikeDegree.Failure,
            CFStrikeDegree.Failure => CFStrikeDegree.Success,
            CFStrikeDegree.Success => CFStrikeDegree.CriticalSuccess,
            _ => CFStrikeDegree.CriticalSuccess,
        };
    }

    private static CFStrikeDegree Worsen(CFStrikeDegree degree)
    {
        return degree switch
        {
            CFStrikeDegree.CriticalSuccess => CFStrikeDegree.Success,
            CFStrikeDegree.Success => CFStrikeDegree.Failure,
            CFStrikeDegree.Failure => CFStrikeDegree.CriticalFailure,
            _ => CFStrikeDegree.CriticalFailure,
        };
    }
}

public enum CFStrikeDegree : byte
{
    CriticalFailure,
    Failure,
    Success,
    CriticalSuccess,
}
