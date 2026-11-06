using Content.Shared.CF.Checks;
using Content.Shared.CF.Traits;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Bonuses and penalties on a creature: timed ones such as bless, shield and heroism, and lasting ones from equipment
/// and character options. Only the bonuses reach the client, for the HUD and sheet. Read totals with <see cref="CFBonusSystem.Total"/>, which applies the remaster stacking rules.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(CFBonusSystem))]
public sealed partial class CFBonusesComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<CFActiveBonus> Bonuses = new();

    /// <summary>Save results made a degree better. Applied by <see cref="CFBonusSystem.UpgradeSave"/>.</summary>
    [DataField]
    public List<CFSaveUpgrade> SaveUpgrades = new();

    /// <summary>The next attack roll is rolled twice and the higher result used (sure strike). Until this time.</summary>
    [DataField]
    public TimeSpan FortuneUntil;

    /// <summary>Non-zero while quickened or slowed has replaced the action cap.</summary>
    [DataField]
    public int ActionCap;

    [DataField]
    public TimeSpan ActionCapUntil;
}

[DataDefinition, Serializable, NetSerializable]
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

    /// <summary>When set, this only counts against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>When set on <see cref="CFBonusStat.SkillChecks"/>, this only counts for this skill.</summary>
    [DataField]
    public string? Skill;
}

/// <summary>One save result that becomes a degree better, such as a success against fear.</summary>
[DataDefinition]
public sealed partial class CFSaveUpgrade
{
    /// <summary>Null means every save.</summary>
    [DataField]
    public CFDefenseType? Save;

    /// <summary>When set, this only works against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    [DataField]
    public CFDegree From = CFDegree.Success;
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

    /// <summary>Every damage roll: melee, ranged and spells. Counts as one stat with <see cref="MeleeDamage"/> for stacking.</summary>
    Damage,

    /// <summary>One save. Counts as one stat with <see cref="Saves"/> for stacking.</summary>
    Fortitude,
    Reflex,
    Will,
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
