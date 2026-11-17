// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.CF.ZLevels.Core.Components;

/// <summary>
/// Component that stores Z-level network configuration for a station, including maps to load above and below
/// the main station level and shared component overrides applied across all Z-levels.
/// </summary>
[RegisterComponent]
public sealed partial class CFStationZLevelsComponent : Component
{
    [DataField]
    public EntityUid? ZNetworkEntity;

    /// <summary>
    /// CrystallEdge: Additional maps loaded below the main map (at negative depth levels).
    /// Each map in the list is loaded at depth -N, -N+1, ..., -1, with <see cref="ResPath"/> at depth 0.
    /// </summary>
    [DataField]
    public List<ResPath> MapsBelow = new();

    /// <summary>
    /// CrystallEdge: additional maps loaded above the main map (at positive depth levels).
    /// Each map in the list is loaded at depth 1, 2, ..., N. <see cref="ResPath"/> works as depth 0.
    /// </summary>
    [DataField]
    public List<ResPath> MapsAbove = new();

    /// <summary>
    /// CrystallEdge: ability to setup shared components for all zLevels
    /// </summary>
    [DataField]
    public ComponentRegistry ZLevelsComponentOverrides = new();
}
