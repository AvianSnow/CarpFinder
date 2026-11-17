using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.CF.Actions;

/// <summary>
/// On a creature whose Rage was started through <see cref="CFActivitySystem.SetRaging"/>, so the system only has to look at
/// those that are raging. The Rage ends at <see cref="EndsAt"/>, or sooner if the creature drops.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CFRagingComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndsAt;

    /// <summary>This Rage leaves the creature fatigued when it ends, because Second Wind took its temporary Hit Points early.</summary>
    [DataField]
    public bool Tiring;
}
