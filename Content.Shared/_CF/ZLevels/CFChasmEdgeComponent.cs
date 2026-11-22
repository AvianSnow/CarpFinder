using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.CF.ZLevels;

/// <summary>
/// A chasm a creature does not drop into the moment it touches. It slips once its centre is over the hole, floats for
/// <see cref="Grace"/> so it can get back to solid ground, and then tries Grab an Edge. Items still fall at once.
/// <see cref="CFChasmEdgeSystem"/> runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFChasmEdgeComponent : Component
{
    /// <summary>How long a slipping creature floats before it has to grab the edge.</summary>
    [DataField]
    public TimeSpan Grace = TimeSpan.FromSeconds(4);

    /// <summary>The Reflex DC of Grab an Edge. 10 is the untrained simple DC.</summary>
    [DataField]
    public int GrabDc = 10;

    /// <summary>How far away the solid ground a successful grab pulls the creature onto can be.</summary>
    [DataField]
    public float LeapFeet = 10;
}

/// <summary>
/// A creature touching a chasm with <see cref="CFChasmEdgeComponent"/>. While <see cref="Slipping"/> it is weightless,
/// so it drifts and can steer itself back. It goes once nothing of its body is over a chasm.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class CFChasmSlipComponent : Component
{
    /// <summary>The chasm under its centre, the one it falls into.</summary>
    [ViewVariables]
    public EntityUid Chasm;

    [DataField, AutoNetworkedField]
    public bool Slipping;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan SlipEnds;

    [DataField]
    public int GrabDc = 10;

    [DataField]
    public float LeapFeet = 10;
}
