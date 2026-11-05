using Content.Shared.CF.DamageTypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Timed resistances and weaknesses to damage types (resist energy). Server-only.
/// Applied to damage with <see cref="CFBonusSystem.ModifyDamage"/>.
/// </summary>
[RegisterComponent, Access(typeof(CFBonusSystem))]
public sealed partial class CFResistancesComponent : Component
{
    [DataField]
    public List<CFActiveResistance> Entries = new();
}

[DataDefinition]
public sealed partial class CFActiveResistance
{
    [DataField]
    public ProtoId<CFDamageTypePrototype> DamageType;

    [DataField]
    public int Amount;

    /// <summary>True for a weakness: the amount is added to damage of that type.</summary>
    [DataField]
    public bool Weakness;

    /// <summary>Zero means it never ends: a creature's printed resistances and immunities.</summary>
    [DataField]
    public TimeSpan Expires;
}

/// <summary>
/// Persistent damage ticking on a creature. Every 6 seconds (one round) it deals its damage,
/// then a DC 15 flat check ends it.
/// </summary>
[RegisterComponent, Access(typeof(CFBonusSystem))]
public sealed partial class CFPersistentDamageComponent : Component
{
    [DataField]
    public List<CFActivePersistentDamage> Entries = new();

    [DataField]
    public TimeSpan NextTick;
}

[DataDefinition]
public sealed partial class CFActivePersistentDamage
{
    [DataField]
    public ProtoId<CFDamageTypePrototype> DamageType;

    [DataField]
    public int Dice;

    [DataField]
    public int Die;

    [DataField]
    public int Flat;

    /// <summary>Who caused it, for kill credit and dying.</summary>
    public EntityUid? Source;
}
