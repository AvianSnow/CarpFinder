using Content.Shared.CF.Preferences;

namespace Content.Shared.CF.Combat;

///<summary>
/// Represents a melee weapon's damage type and damage dice.
/// </summary>

public static class CFMelee
{
    // <summary>
    /// How far a melee swing reaching, In tiles, One tile in ss14 is 32x32pixels, wich is 1.8m in real life, roughly 6ft.
    /// but we need to make sure that melee weapons can reach a bit further than 1 tile, so we will set the range to 1.5 tiles, wich is 48x48pixels, wich is 2.7m in real life, roughly 9ft.
    /// </summary>
    public const float MeleeRange = 1.5f;

    public static bool InReach(float distance)
    {
        return distance <= MeleeRange;
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