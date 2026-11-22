using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Actions;

/// <summary>Tumble Through state. Added the first time a creature pushes into an enemy.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFTumbleComponent : Component
{
    /// <summary>The enemy being pushed into right now.</summary>
    public EntityUid? PushTarget;

    public TimeSpan PushSince;

    /// <summary>The next attempt waits for this.</summary>
    public TimeSpan NextTumble;

    /// <summary>Mob pushing is off for this creature until then, so it slides through.</summary>
    [AutoNetworkedField]
    public TimeSpan ThroughUntil;
}

/// <summary>Plays the full spin on every client that can see the tumbler.</summary>
[Serializable, NetSerializable]
public sealed class CFTumbleSpinEvent(NetEntity tumbler) : EntityEventArgs
{
    public readonly NetEntity Tumbler = tumbler;
}
