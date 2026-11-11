using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.CF.Upkeep;

/// <summary>
/// On a body a player spawned into. Holds the job slot it took, so the slot comes back when the body is deleted,
/// and how long the body has been left without its player.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CFPlayerBodyComponent : Component
{
    [DataField]
    public EntityUid? Station;

    [DataField]
    public ProtoId<JobPrototype>? Job;

    /// <summary>The player who spawned in it, so respawning finds it even after their mind has left it.</summary>
    [ViewVariables]
    public NetUserId? User;

    /// <summary>Set once the slot has been given back, so it is never given back twice.</summary>
    [DataField]
    public bool SlotReturned;

    /// <summary>When the player left this body. Null while a player is in it.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? LeftAt;

    /// <summary>How long a body may stand empty before it is removed.</summary>
    [DataField]
    public TimeSpan SsdLimit = TimeSpan.FromMinutes(15);
}
