// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.Tools.Components;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.ZLevels.Tiles;

/// <summary>
/// Marker for entities that also have <see cref="ToolTileCompatibleComponent"/>, allowing them to
/// deconstruct the tile on the z-level directly above the wielder while <see cref="Content.Shared.CF.ZLevels.Core.Components.CFZLevelViewerComponent.LookUp"/>
/// is enabled. Reuses <see cref="ToolTileCompatibleComponent.Delay"/> and <see cref="ToolTileCompatibleComponent.RequiresUnobstructed"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(CFZLevelToolTileSystem))]
public sealed partial class CFZLevelToolTileComponent : Component;
