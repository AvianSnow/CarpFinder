// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.ZLevels.Core.Components;

/// <summary>
/// Tracker that tracks all maps added to the zLevel network. Usually, entity in Nullspace,
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFZMapNetworkComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public readonly Dictionary<int, EntityUid?> ZLevels = new();

    /// <summary>
    /// Shared components for all zLevels maps
    /// </summary>
    [DataField(serverOnly: true)]
    public ComponentRegistry Components = new();

    /// <remarks>
    /// Reversed version of <see cref="ZLevels"/>
    /// </remarks>
    [ViewVariables, AutoNetworkedField]
    public readonly Dictionary<EntityUid, int> ZLevelByEntity = new();

    [ViewVariables, AutoNetworkedField]
    public readonly List<EntityUid> SortedZLevels = new();

    [ViewVariables, AutoNetworkedField]
    public int SortedMin = 0;

    [ViewVariables, AutoNetworkedField]
    public int SortedMax = 0;
}
