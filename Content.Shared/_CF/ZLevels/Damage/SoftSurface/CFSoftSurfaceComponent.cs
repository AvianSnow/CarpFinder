// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Robust.Shared.GameStates;

namespace Content.Shared.CF.ZLevels.Damage.SoftSurface;

/// <summary>
/// Something soft to land on. A creature that falls onto it takes this fraction of the fall damage: 0 is a landing that costs nothing
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFSoftSurfaceComponent : Component
{
    [DataField, AutoNetworkedField]
    public float DamageMultiplier = 0f;

    [DataField, AutoNetworkedField]
    public float StunMultiplier = 0f;
}
