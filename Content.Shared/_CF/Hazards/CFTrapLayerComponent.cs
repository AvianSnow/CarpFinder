using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Hazards;

/// <summary>
/// A creature that builds traps, as a stat block's Construct Trap. A player lays one from the wheel, and a brain through
/// the <c>LayTrap</c> step. Both end in <see cref="CFTrapLayerSystem.TryLay"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFTrapLayerComponent : Component
{
    /// <summary>The hazard it builds.</summary>
    [DataField(required: true)]
    public EntProtoId Trap;

    /// <summary>Traps it has the materials for.</summary>
    [DataField, AutoNetworkedField]
    public int Charges = 6;

    /// <summary>Actions a brain pays for one. A player pays through the action's <c>CFRadialSlot</c>.</summary>
    [DataField]
    public int Actions = 3;

    /// <summary>How long a brain waits between traps, so it still shoots.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(12);

    /// <summary>How far apart a brain keeps its traps, in tiles.</summary>
    [DataField]
    public float SpacingTiles = 3f;

    /// <summary>When a brain may lay the next one.</summary>
    [ViewVariables]
    public TimeSpan NextLay;
}
