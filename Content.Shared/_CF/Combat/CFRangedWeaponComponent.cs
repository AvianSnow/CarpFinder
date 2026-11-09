using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// A <see cref="CFWeaponComponent"/> that shoots. Projectiles fired from it resolve remaster Strikes on hit.
/// Ammunition is the stock SS14 projectile pipeline (arrows nocked into the bow's slot, bolts, and so on).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFRangedWeaponComponent : Component
{
    /// <summary>Range increment in feet (longbow 100). Each increment past the first is −2; past the sixth you cannot hit.</summary>
    [DataField, AutoNetworkedField]
    public int RangeIncrementFeet = 100;

    /// <summary>Actions spent reloading after each shot, on top of the Strike itself (crossbow 1, heavy crossbow 2).</summary>
    [DataField, AutoNetworkedField]
    public int Reload;

    /// <summary>A slinger's reload has already paid for the next shot's reload.</summary>
    [DataField, AutoNetworkedField]
    public bool ReloadPrepaid;
}
