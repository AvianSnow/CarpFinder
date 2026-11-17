// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.ZLevels.Core.Components;

/// <summary>
/// Allows entity to see through Z-levels
/// </summary>
[UnsavedComponent]
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
[Access(typeof(CFSharedZLevelsSystem))]
public sealed partial class CFZLevelViewerComponent : Component
{
    public HashSet<EntityUid> Eyes = new();

    /// <summary>
    /// We can look at 1 z-level up.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool LookUp;

    #region Actions

    [DataField]
    public EntProtoId ActionId = "CFActionToggleLookUp";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    #endregion
}
