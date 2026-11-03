using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Weapons.Melee;

namespace Content.Shared.CF.Companions;

/// <summary>
/// A carried item that is not a weapon leaves the ally's bite in place.
/// </summary>
public sealed class CFCompanionHandSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CFCompanionComponent, GetMeleeWeaponEvent>(OnGetWeapon);
    }

    private void OnGetWeapon(EntityUid uid, CFCompanionComponent comp, GetMeleeWeaponEvent args)
    {
        if (args.Handled || !_hands.TryGetActiveItem(uid, out var held))
            return;

        if (HasComp<VirtualItemComponent>(held))
            return;

        if (TryComp(held, out MeleeWeaponComponent? melee) && !melee.MustBeEquippedToUse)
            return;

        args.Weapon = uid;
        args.Handled = true;
    }
}
