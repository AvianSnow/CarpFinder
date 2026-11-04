using Content.Shared.CF.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Snapshot of a weapon for one Strike: dice after runes and two-hand, damage type, proficiency,
/// and the weapon's traits resolved from their <c>cfTrait</c> rules.
/// Built by <see cref="CFWeaponSystem.Stats"/> or <see cref="CFStrikeSystem.UnarmedStats"/>.
/// </summary>
public readonly record struct CFStrikeWeaponStats(
    int DamageDice,
    int DamageDie,
    string DamageType,
    string Proficiency,
    CFWeaponTraits Traits,
    bool Ranged = false,
    int RangeIncrementFeet = 0,
    int Potency = 0,
    int Striking = 0,
    int Reload = 0,
    bool ThrownStrike = false)
{
    public bool Agile => Traits.Agile;
    public bool Finesse => Traits.Finesse;
}

/// <summary>
/// A weapon's traits, read once from the trait prototypes. Values are in feet or die faces.
/// </summary>
public sealed record CFWeaponTraits
{
    public static readonly CFWeaponTraits None = new();

    public bool Agile { get; init; }
    public bool Finesse { get; init; }
    public bool Forceful { get; init; }
    public bool Sweep { get; init; }
    public bool Backstabber { get; init; }
    public bool Propulsive { get; init; }
    public bool Nonlethal { get; init; }
    public bool Unarmed { get; init; }
    public bool Trip { get; init; }
    public bool Shove { get; init; }
    public bool Disarm { get; init; }
    public bool Grapple { get; init; }

    /// <summary>Reach in feet. 0 when the weapon lacks the trait.</summary>
    public int Reach { get; init; }

    /// <summary>Thrown range increment in feet. 0 when the weapon cannot be thrown.</summary>
    public int Thrown { get; init; }

    /// <summary>−2 to attacks against targets this close, in feet.</summary>
    public int Volley { get; init; }

    /// <summary>Extra die size on a critical hit.</summary>
    public int Deadly { get; init; }

    /// <summary>On a critical hit the weapon's dice become this size, plus one extra die of it.</summary>
    public int Fatal { get; init; }

    /// <summary>Die size when wielded in two hands.</summary>
    public int TwoHand { get; init; }

    /// <summary>Damage types the weapon can deal instead of its usual one.</summary>
    public IReadOnlyList<string> Versatile { get; init; } = Array.Empty<string>();

    /// <summary>Unarmed attacks (fists): agile, finesse, nonlethal, unarmed.</summary>
    public static readonly CFWeaponTraits Fist = new()
    {
        Agile = true,
        Finesse = true,
        Nonlethal = true,
        Unarmed = true,
    };

    public static CFWeaponTraits From(IPrototypeManager prototypes, IEnumerable<ProtoId<CFTraitPrototype>> traits)
    {
        var result = new CFWeaponTraits();
        List<string>? versatile = null;
        foreach (var id in traits)
        {
            if (!prototypes.TryIndex(id, out var trait))
                continue;

            result = trait.Rule switch
            {
                CFTraitRule.Agile => result with { Agile = true },
                CFTraitRule.Finesse => result with { Finesse = true },
                CFTraitRule.Forceful => result with { Forceful = true },
                CFTraitRule.Sweep => result with { Sweep = true },
                CFTraitRule.Backstabber => result with { Backstabber = true },
                CFTraitRule.Propulsive => result with { Propulsive = true },
                CFTraitRule.Nonlethal => result with { Nonlethal = true },
                CFTraitRule.Unarmed => result with { Unarmed = true },
                CFTraitRule.Trip => result with { Trip = true },
                CFTraitRule.Shove => result with { Shove = true },
                CFTraitRule.Disarm => result with { Disarm = true },
                CFTraitRule.Grapple => result with { Grapple = true },
                CFTraitRule.Reach => result with { Reach = Math.Max(result.Reach, trait.Value) },
                CFTraitRule.Thrown => result with { Thrown = Math.Max(result.Thrown, trait.Value) },
                CFTraitRule.Volley => result with { Volley = Math.Max(result.Volley, trait.Value) },
                CFTraitRule.Deadly => result with { Deadly = Math.Max(result.Deadly, trait.Value) },
                CFTraitRule.Fatal => result with { Fatal = Math.Max(result.Fatal, trait.Value) },
                CFTraitRule.TwoHand => result with { TwoHand = Math.Max(result.TwoHand, trait.Value) },
                _ => result,
            };

            if (trait.Rule == CFTraitRule.Versatile && trait.DamageType is { } type)
                (versatile ??= new List<string>()).Add(type.Id);
        }

        return versatile == null ? result : result with { Versatile = versatile };
    }
}
