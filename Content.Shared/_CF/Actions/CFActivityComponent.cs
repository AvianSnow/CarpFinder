using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.Actions;

public enum CFActivityKind : byte
{
    Demoralize,
    Trip,
    Shove,
    Grapple,
    Feint,
    RaiseShield,
    Rage,
    HuntPrey,
    Flurry,
    Spellstrike,
    Stratagem,
    Exploit,
    Inspire,
    Finisher,
    DoubleSlice,
    ExactingStrike,
    SuddenCharge,
    SnaggingStrike,
    ViciousSwing,
    PointBlank,
    TwinFeint,
    TumbleBehind,
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
