using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// A <see cref="CFWeaponComponent"/> used for melee Strikes instead of stock SS14 damage.
/// On a creature itself, it is the creature's natural weapon (jaws, claws, a kobold's thrust).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFMeleeWeaponComponent : Component
{
    /// <summary>
    /// Reach in feet without traits. 5 threatens adjacent tiles. The reach trait raises it to its value,
    /// and the longer reach also lengthens the SS14 swing so the weapon can Strike at that distance.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ReachFeet = CFFlanking.DefaultReachFeet;
}
