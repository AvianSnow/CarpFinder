// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Robust.Shared.GameStates;

namespace Content.Shared.CF.ZLevels.Chasm;

/// <summary>
/// Marks a Z-level map as having a lethal abyss below it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFZLevelChasmComponent : Component;
