using Robust.Shared.GameStates;

namespace Content.Shared.CF.Actions;

/// <summary>
/// An action that can only be used so many times a day. On the action entity. A long rest gives the uses back
/// (<see cref="CFDailyUseSystem.Restore"/>), as it does for reactions with <c>perDay</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFDailyUsesComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Uses = 1;

    [DataField, AutoNetworkedField]
    public int Used;

    public bool Spent => Used >= Uses;
}
