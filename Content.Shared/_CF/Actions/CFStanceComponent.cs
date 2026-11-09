using Content.Shared.CF.Effects;
using Content.Shared.CF.Traits;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Makes an action a stance. What the stance does is all data here, read by <see cref="CFStanceSystem"/>.
/// Parent an action on <c>CFActStanceBase</c> and grant it with <c>CFGrantAction</c>.
/// </summary>
[RegisterComponent]
public sealed partial class CFStanceComponent : Component
{
    /// <summary>The stance can only be entered, and only lasts, while no armor is worn.</summary>
    [DataField]
    public bool RequiresUnarmored;

    /// <summary>Lasts as long as the stance does.</summary>
    [DataField]
    public List<CFStanceBonus> Bonuses = new();

    /// <summary>Replaces the creature's fists while in the stance.</summary>
    [DataField]
    public CFStanceAttack? Attack;

    /// <summary>The stance's <see cref="Attack"/> is the only Strike the creature can make.</summary>
    [DataField]
    public bool OnlyStanceStrikes;

    /// <summary>The most Dexterity that counts toward AC.</summary>
    [DataField]
    public int? DexCap;

    /// <summary>Ignore volley and add 2 damage inside the first range increment (Point-Blank Stance).</summary>
    [DataField]
    public bool PointBlank;
}

[DataDefinition]
public sealed partial class CFStanceBonus
{
    [DataField(required: true)]
    public CFBonusStat Stat;

    [DataField(required: true)]
    public CFBonusType Type;

    /// <summary>Negative for a penalty.</summary>
    [DataField(required: true)]
    public int Value;
}

[DataDefinition]
public sealed partial class CFStanceAttack
{
    [DataField]
    public int Dice = 1;

    [DataField(required: true)]
    public int Die;

    [DataField(required: true)]
    public string DamageType = string.Empty;

    [DataField]
    public List<ProtoId<CFTraitPrototype>> Traits = new();
}

/// <summary>On a creature that has used a stance. It stays after the stance ends, to keep the wait for the next one.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFActiveStanceComponent : Component
{
    /// <summary>The action prototype of the stance the creature is in. Null when it is in none.</summary>
    [DataField, AutoNetworkedField]
    public EntProtoId? Stance;

    /// <summary>A stance action cannot be used again before this time.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan NextStance;
}
