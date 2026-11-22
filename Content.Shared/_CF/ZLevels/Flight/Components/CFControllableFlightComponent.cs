// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.ZLevels.Flight.Components;

/// <summary>
/// Allows an entity to control its own flight status
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true),
 Access(typeof(CFSharedZFlightSystem))]
public sealed partial class CFControllableFlightComponent : Component
{
    [DataField]
    public EntProtoId UpActionProto = "CFActionZFlightUp";

    [DataField, AutoNetworkedField]
    public EntityUid? ZLevelUpActionEntity;

    [DataField]
    public EntProtoId DownActionProto = "CFActionZFlightDown";

    [DataField, AutoNetworkedField]
    public EntityUid? ZLevelDownActionEntity;

    [DataField]
    public EntProtoId ToggleActionProto = "CFActionZFlightToggle";

    [DataField, AutoNetworkedField]
    public EntityUid? ZLevelToggleActionEntity;

    [DataField]
    public TimeSpan? StartFlightDoAfter;
}
