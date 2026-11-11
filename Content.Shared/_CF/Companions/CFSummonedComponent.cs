using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Companions;

/// <summary>
/// A creature called by a spell or ability, such as a thrall. It carries the Summoned trait. When it dies, or its
/// time is up, it vanishes in a puff of smoke and leaves no body, so it cannot be looted or harvested.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFSummonedComponent : Component
{
    /// <summary>What it leaves behind as it goes.</summary>
    [DataField]
    public EntProtoId Puff = "CFSummonPuff";
}
