namespace Content.Shared.CF.Effects;

/// <summary>
/// Timed bonuses and penalties on a creature: bless, shield, mystic armor, heroism, runic weapon, haste.
/// Server-only. Read totals with <see cref="CFBonusSystem.Total"/>, which applies the remaster stacking rules.
/// </summary>
[RegisterComponent, Access(typeof(CFBonusSystem))]
public sealed partial class CFBonusesComponent : Component
{
    [DataField]
    public List<CFActiveBonus> Bonuses = new();

    /// <summary>The next attack roll is rolled twice and the higher result used (sure strike). Until this time.</summary>
    [DataField]
    public TimeSpan FortuneUntil;

    /// <summary>Non-zero while quickened or slowed has replaced the action cap.</summary>
    [DataField]
    public int ActionCap;

    [DataField]
    public TimeSpan ActionCapUntil;
}

[DataDefinition]
public sealed partial class CFActiveBonus
{
    [DataField]
    public CFBonusStat Stat;

    [DataField]
    public CFBonusType Type;

    /// <summary>Negative for a penalty.</summary>
    [DataField]
    public int Value;

    [DataField]
    public TimeSpan Expires;

    /// <summary>Set for bonuses from worn equipment. They last until the equipment changes, not for a time.</summary>
    [DataField]
    public CFEquipmentBonusSlot? Equipment;
}

/// <summary>Where a lasting equipment bonus comes from, so re-equipping replaces it instead of adding another.</summary>
public enum CFEquipmentBonusSlot : byte
{
    ArmorPotency,
    ArmorResilient,
}

/// <summary>What a bonus applies to.</summary>
public enum CFBonusStat : byte
{
    ArmorClass,
    AttackRoll,

    /// <summary>All saving throws.</summary>
    Saves,
    Perception,
    SkillChecks,

    /// <summary>Damage on melee Strikes (enlarge).</summary>
    MeleeDamage,

    /// <summary>Extra weapon damage dice on Strikes (runic weapon). Not a typed bonus: the highest applies.</summary>
    WeaponDice,
}

/// <summary>Bonuses of the same type do not stack: the highest bonus and the worst penalty of each type apply.</summary>
public enum CFBonusType : byte
{
    Status,
    Circumstance,
    Item,

    /// <summary>Untyped bonuses and penalties all stack.</summary>
    Untyped,
}
