using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Placeable;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Whitelist;

namespace Content.Shared.CF.Cooking;

/// <summary>
/// Sets on a <see cref="CFCookingSurfaceComponent"/> whatever its fuel store would refuse, the way a table takes an
/// item, so the fire under it can heat it.
/// </summary>
public sealed partial class CFCookingSurfaceSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// The store would refuse this, so it does not get to claim the click: the interaction falls through to
    /// <see cref="OnAfterInteractUsing"/>. Fuel still goes in.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnStorageAttempt(Entity<CFCookingSurfaceComponent> ent, ref StorageInteractUsingAttemptEvent args)
    {
        if (args.Cancelled || !TryComp(ent, out StorageComponent? storage))
            return;

        if (_whitelist.IsWhitelistFail(storage.Whitelist, args.Used) || _whitelist.IsWhitelistPass(storage.Blacklist, args.Used))
            args.Cancelled = true;
    }

    /// <summary>Nothing else wanted the click, so the item is set on the fire.</summary>
    [SubscribeLocalEvent]
    private void OnAfterInteractUsing(Entity<CFCookingSurfaceComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || !HasComp<ItemComponent>(args.Used))
            return;

        if (TryComp(ent, out ItemPlacerComponent? placer)
            && placer.MaxEntities != 0
            && placer.PlacedEntities.Count >= placer.MaxEntities)
        {
            _popup.PopupEntity(Loc.GetString("cf-cooking-surface-full", ("surface", ent.Owner)), ent, args.User);
            args.Handled = true;
            return;
        }

        if (!_hands.TryDrop(args.User, args.Used))
            return;

        _transform.SetCoordinates(args.Used, Transform(ent).Coordinates);
        args.Handled = true;
    }
}
