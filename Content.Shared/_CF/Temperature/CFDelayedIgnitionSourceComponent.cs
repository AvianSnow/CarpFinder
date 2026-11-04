using Robust.Shared.GameStates;

namespace Content.Shared.CF.Temperature;

/// <summary>
/// Allows you to fire entities through interacting with them after a delay.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(CFSharedFireSpreadSystem))]
public sealed partial class CFDelayedIgnitionSourceComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(3f);
}
