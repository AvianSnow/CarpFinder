using Content.Shared.CF.DamageTypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Traits;

/// <summary>
/// A Carpfinder trait. Ids match the names used on spells and items, such as Agile or Fire.
/// Summaries are short mechanical notes, not source-book text.
/// </summary>
[Prototype("cfTrait")]
public sealed partial class CFTraitPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name { get; private set; } = "";

    [DataField]
    public CFTraitCategory Category;

    [DataField]
    public string Summary { get; private set; } = "";

    /// <summary>
    /// What the game does with this trait. <see cref="CFTraitRule.None"/> traits are labels only.
    /// Weapon rules are applied by <c>CFStrikeSystem</c>, armor rules by <c>CFArmorSystem</c>.
    /// </summary>
    [DataField]
    public CFTraitRule Rule = CFTraitRule.None;

    /// <summary>The number in the trait: deadly d10 is 10, reach 10 is 10, thrown 20 is 20, volley 30 is 30.</summary>
    [DataField]
    public int Value;

    /// <summary>The damage type a versatile trait switches to.</summary>
    [DataField]
    public ProtoId<CFDamageTypePrototype>? DamageType;
}

/// <summary>Traits with a game rule. Values come from <see cref="CFTraitPrototype.Value"/>.</summary>
public enum CFTraitRule : byte
{
    None,

    // Weapons
    Agile,
    Finesse,
    Forceful,
    Sweep,
    Backstabber,
    Propulsive,
    Nonlethal,
    Unarmed,
    Reach,
    Thrown,
    Volley,
    Deadly,
    Fatal,
    TwoHand,
    Versatile,
    Trip,
    Shove,
    Disarm,
    Grapple,
    Concussive,
    Kickback,
    Scatter,

    // Armor
    Bulwark,
    Comfort,
    Flexible,
    Noisy,
}

public enum CFTraitCategory
{
    Common,
    Affliction,
    Alignment,
    Ancestry,
    Armor,
    Class,
    ClassSpecific,
    CreatureType,
    Elemental,
    Energy,
    Equipment,
    Feat,
    Hazard,
    Mechanic,
    Monster,
    Planar,
    Poison,
    Rarity,
    School,
    Sense,
    Settlement,
    Shield,
    Tradition,
    Weapon,
}
