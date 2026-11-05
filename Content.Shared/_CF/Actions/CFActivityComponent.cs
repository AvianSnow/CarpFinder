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
    PointBlank,
    Leap,
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
