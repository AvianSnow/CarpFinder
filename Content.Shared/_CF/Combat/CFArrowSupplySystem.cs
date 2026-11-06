using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;

namespace Content.Shared.CF.Combat;

/// <summary>Nocks a monster's next arrow, in the same tick as the shot.</summary>
public sealed partial class CFArrowSupplySystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    /// <summary>
    /// At once and not on a timer: stock HTN drops a gun it finds empty, so a bow must never be seen between arrows.
    /// <c>GunComponent</c> and this event are a taken pair, in <c>CFActionPoolSystem</c>.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShot(Entity<CFRangedWeaponComponent> weapon, ref GunShotEvent args)
    {
        if (_net.IsClient
            || !TryComp(args.User, out CFArrowSupplyComponent? supply)
            || supply.Arrows <= 0
            || !TryComp(weapon, out ContainerAmmoProviderComponent? ammo))
            return;

        if (EntityManager.TrySpawnInContainer(supply.Ammo, weapon, ammo.Container, out _))
            supply.Arrows--;
    }
}
