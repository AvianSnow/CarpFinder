using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.CF.Character;

/// <summary>When this creature can next take a long rest. It is added by the first one.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CFRestCooldownComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ReadyAt;
}
