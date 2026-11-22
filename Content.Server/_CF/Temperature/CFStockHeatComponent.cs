using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.CF.Temperature;

/// <summary>Stock heat a creature has taken this round, waiting to become CF fire damage.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CFStockHeatComponent : Component
{
    /// <summary>In stock Heat, not hit points.</summary>
    [DataField]
    public float Pending;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextTick;
}
