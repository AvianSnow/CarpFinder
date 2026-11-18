using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.Actions;

public enum CFActivityKind : byte
{
    Rage,
    Flurry,
    Spellstrike,
    Anthem,
    SuddenCharge,
    Leap,
    ConstructTrap,
    SpellstrikeRecharge,
    MagusAnalysis,

    /// <summary>Venomtail Kobold: the next piercing or slashing hit within 6 seconds deals persistent poison.</summary>
    TailToxin,

    /// <summary>Envenom Fangs: the next unarmed hit within 6 seconds deals 1d6 persistent poison.</summary>
    EnvenomFangs,

    /// <summary>Demoralize the target, at whatever range the action allows (Terrifying Croak).</summary>
    Demoralize,

    /// <summary>Feint the target, at whatever range the action allows (Grovel).</summary>
    Feint,

    /// <summary>A Feint whose success makes the target take -2 to attacks against the user instead of leaving it off-guard (Overextending Feint).</summary>
    OverextendingFeint,

    /// <summary>Left-Hand Blood: 1 slashing damage to the user, and the held weapon's next hit within 6 seconds deals 1d4 persistent poison.</summary>
    LeftHandBlood,

    /// <summary>Spark Fist: for 6 seconds a fist hit is lethal and deals fire damage to the target and to the user.</summary>
    SparkFist,
}

/// <summary>
/// One radial activity. The kind selects the handler. The radial slot pays the action cost.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFActivityComponent : Component
{
    [DataField, AutoNetworkedField]
    public CFActivityKind Kind;
}

public sealed partial class CFActivityInstantEvent : InstantActionEvent
{
}

public sealed partial class CFActivityTargetEvent : EntityTargetActionEvent
{
}
