using Robust.Shared.GameStates;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Puts an action on the radial wheel. Spell rank 0 is a cantrip.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFRadialSlotComponent : Component
{
    [DataField, AutoNetworkedField]
    public CFRadialGroup Group = CFRadialGroup.Action;

    /// <summary>
    /// Used when <see cref="Group"/> is <see cref="CFRadialGroup.Spell"/>. 0 is a cantrip.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int SpellRank;
}

public enum CFRadialGroup : byte
{
    /// <summary>An activity that costs one, two, or three actions.</summary>
    Action,

    Reaction,

    Free,

    Spell,
}
