using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Robust.Shared.Random;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Carpfinder 2e Strike helpers: attack and damage modifiers, range penalties, damage dice.
/// Degrees of success live in <see cref="Content.Shared.CF.Checks.CFDegrees"/>.
/// </summary>
public static class CFMelee
{
    /// <summary>
    /// Melee reach in tiles. One SS14 tile is ~1.8 m (~6 ft); 1.25 covers adjacent + a little reach.
    /// </summary>
    public const float MeleeRange = 1.25f;

    /// <summary>Default PC / commoner fist Strike (remaster unarmed): 1d4 bludgeoning, agile, finesse, nonlethal.</summary>
    public const int UnarmedDamageDice = 1;
    public const int UnarmedDamageDie = 4;
    public const string UnarmedDamageType = "Bludgeoning";
    public const string UnarmedProficiency = "Unarmed";

    /// <summary>Approximate PF feet per SS14 tile (1 tile ≈ 1.5 m ≈ 5 ft grid square).</summary>
    public const float FeetPerTile = 5f;

    /// <summary>
    /// Multiple Attack Penalty for an attack roll. <paramref name="attacksAlreadyMade"/> is how many
    /// attack rolls this creature has made since its action pool was last full.
    /// </summary>
    public static int MapPenalty(int attacksAlreadyMade, bool agile)
    {
        var step = Math.Clamp(attacksAlreadyMade, 0, 2);
        if (agile)
            return step == 0 ? 0 : step == 1 ? -4 : -8;

        return step == 0 ? 0 : step == 1 ? -5 : -10;
    }

    public static bool InReach(float distance) => distance <= MeleeRange;

    public static float TilesToFeet(float tiles) => tiles * FeetPerTile;

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

    /// <summary>Rolls dice + modifier. <paramref name="critical"/> doubles the dice only (spell code doubles totals itself).</summary>
    public static int RollDamage(IRobustRandom random, int diceCount, int dieFaces, int damageMod, bool critical)
    {
        var dice = critical ? diceCount * 2 : diceCount;
        var total = damageMod;
        for (var i = 0; i < dice; i++)
        {
            total += random.Next(1, dieFaces + 1);
        }

        return Math.Max(0, total);
    }

    /// <summary>
    /// Attack bonus for a Strike before item bonuses, conditions, and the multiple attack penalty.
    /// Ranged and thrown use Dexterity. Melee uses Strength, or the better of Strength and Dexterity with finesse.
    /// </summary>
    public static int AttackBonus(
        CFAbilityScoreComponent abilities,
        CFCombatantComponent combatant,
        CFStrikeWeaponStats weapon,
        bool weaponTrained,
        float distanceFeet = 0f,
        bool ignoreVolley = false)
    {
        var proficiency = CFAttributeMath.ProficiencyBonus(combatant.Level, weaponTrained);
        int ability;
        if (weapon.Ranged)
            ability = abilities.GetModifier(CFAbility.Dexterity);
        else if (weapon.Finesse)
            ability = Math.Max(abilities.GetModifier(CFAbility.Strength), abilities.GetModifier(CFAbility.Dexterity));
        else
            ability = abilities.GetModifier(CFAbility.Strength);

        var bonus = ability + proficiency + combatant.StrikeAdjustment;
        bonus += RangeIncrementPenalty(distanceFeet, weapon.RangeIncrementFeet);
        if (!ignoreVolley)
            bonus += VolleyPenalty(distanceFeet, weapon.Traits.Volley);
        return bonus;
    }

    /// <summary>
    /// Damage modifier. Melee and thrown add Strength. Propulsive adds half a positive Strength
    /// (all of a negative one). Other ranged weapons add nothing.
    /// </summary>
    public static int DamageBonus(CFAbilityScoreComponent abilities, CFStrikeWeaponStats weapon)
    {
        var str = abilities.GetModifier(CFAbility.Strength);
        if (!weapon.Ranged || weapon.ThrownStrike)
            return str;

        if (!weapon.Traits.Propulsive)
            return 0;

        return str < 0 ? str : str / 2;
    }

    /// <summary>
    /// Forceful: +1 damage per weapon die on your second attack this turn, +2 per die on the third and later.
    /// </summary>
    public static int ForcefulBonus(CFStrikeWeaponStats weapon, int attacksAlreadyMade)
    {
        if (!weapon.Traits.Forceful || attacksAlreadyMade <= 0)
            return 0;

        return weapon.DamageDice * Math.Min(attacksAlreadyMade, 2);
    }

    /// <summary>−2 per range increment beyond the first. Attacks beyond 6 increments are impossible (−999).</summary>
    public static int RangeIncrementPenalty(float distanceFeet, int rangeIncrementFeet)
    {
        if (rangeIncrementFeet <= 0 || distanceFeet <= 0f)
            return 0;

        // First increment is 0..range (no penalty). Each further multiple is −2.
        var incrementIndex = (int)Math.Ceiling(distanceFeet / rangeIncrementFeet);
        if (incrementIndex <= 1)
            return 0;

        if (incrementIndex > 6)
            return -999;

        return -2 * (incrementIndex - 1);
    }

    /// <summary>Volley trait: −2 when the target is within the listed distance.</summary>
    public static int VolleyPenalty(float distanceFeet, int volleyFeet)
    {
        if (volleyFeet <= 0)
            return 0;

        return distanceFeet <= volleyFeet ? -2 : 0;
    }

    /// <summary>
    /// Weapon damage for a hit. A critical hit doubles everything rolled here, including
    /// <paramref name="extraDamage"/> (precision, finisher). Deadly and fatal dice are added after doubling.
    /// </summary>
    public static int RollStrikeDamage(
        IRobustRandom random,
        CFStrikeWeaponStats weapon,
        int damageMod,
        bool critical,
        int extraDamage = 0)
    {
        var die = critical && weapon.Traits.Fatal > 0 ? weapon.Traits.Fatal : weapon.DamageDie;
        var total = RollDamage(random, weapon.DamageDice, die, damageMod, critical: false) + extraDamage;
        if (!critical)
            return Math.Max(0, total);

        total *= 2;

        // Fatal adds one more die of the fatal size.
        if (weapon.Traits.Fatal > 0)
            total += random.Next(1, weapon.Traits.Fatal + 1);

        // Deadly adds one die, two with greater striking, three with major striking.
        if (weapon.Traits.Deadly > 0)
        {
            var dice = Math.Max(1, weapon.Striking);
            for (var i = 0; i < dice; i++)
            {
                total += random.Next(1, weapon.Traits.Deadly + 1);
            }
        }

        return Math.Max(0, total);
    }
}
