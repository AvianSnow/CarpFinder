using Content.Shared.CF.Preferences;

namespace Content.Shared.CF.Combat;

///<summary>
/// Represents a melee weapon's damage type and damage dice.
/// </summary>

public static class CFMelee
{
    // <summary>
    /// How far a melee swing reaching, In tiles, One tile in ss14 is 32x32pixels, wich is 1.8m in real life, roughly 6ft.
    /// but we need to make sure that melee weapons can reach a bit further than 1 tile, so we will set the range to 1.25 tiles,
    public const float MeleeRange = 1.25f;

    public static bool InReach(float distance)
    {
        return distance <= MeleeRange;
    }
    public static int AttackAbility(int strength, int dexterity, int proficiencyBonus, bool isProficient, bool finesse)
    {
        // If the weapon is a finesse weapon, we can use either strength or dexterity, whichever is higher.
        // For now we will just use strength, but we will add finesse weapons later.
        int abilityScore = Math.Max(strength, dexterity);
        int abilityModifier = (abilityScore - 10) / 2;
        return abilityModifier + (isProficient ? proficiencyBonus : 0);
    }
    public static bool Hits(int attackRoll, int targetAC)
    {
        return attackRoll >= targetAC;
    }
    public static int DamageRoll(int damageDice, int damageModifier)
    {
        // Roll the damage dice and add the damage modifier.
        System.Random rand = new System.Random();
        int roll = rand.Next(1, damageDice + 1);
        return roll + damageModifier;
    }


    public static readonly Dictionary<string, string> DamageTypes = new()
    {
        { "slashing", "Slashing" },
        { "piercing", "Piercing" },
        { "bludgeoning", "Bludgeoning" }
    };

    public static readonly Dictionary<string, string> DamageDice = new()
    {
        { "d4", "D4" },
        { "d6", "D6" },
        { "d8", "D8" },
        { "d10", "D10" },
        { "d12", "D12" }
    };
}