using Content.Shared.CF.Actions;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Network;

namespace Content.Shared.CF.Combat;

/// <summary>
/// An action that loads the held weapon from what the user carries. <see cref="CFReloadSystem"/> runs it. With
/// <see cref="Then"/> it is a slinger's reload: the basic action is paid for, and the reload comes free with it.
/// </summary>
[RegisterComponent]
public sealed partial class CFReloadActionComponent : Component
{
    [DataField]
    public CFBasicAction? Then;
}

/// <summary>
/// Loads a held crossbow or firearm from carried ammunition. Shots pay for their reload as they are fired, so loading itself is
/// free. A slinger's reload has already paid for it, which <see cref="CFRangedWeaponComponent.ReloadPrepaid"/> remembers.
/// </summary>
public sealed partial class CFReloadSystem : EntitySystem
{
    private const string Slot = "projectiles";

    /// <summary>A bag in a bag is as deep as anyone reaches in a fight.</summary>
    private const int MaxDepth = 2;

    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private CFManeuverSystem _maneuvers = default!;

    /// <summary>Loads the held weapon if it is empty. With <paramref name="prepaid"/>, its next shot does not pay for a reload.</summary>
    public bool TryReload(EntityUid user, bool prepaid)
    {
        if (!TryHeld(user, out var gun, out var ranged) || !_slots.TryGetSlot(gun, Slot, out var slot))
        {
            _popup.PopupEntity(Loc.GetString("cf-reload-no-weapon"), user, user);
            return false;
        }

        if (slot.HasItem)
        {
            Prepay(gun, ranged, prepaid);
            return true;
        }

        if (!TryFindRound(user, slot, out var round) || !_slots.TryInsert(gun, slot, round, user))
        {
            _popup.PopupEntity(Loc.GetString("cf-reload-no-ammo"), user, user);
            return false;
        }

        Prepay(gun, ranged, prepaid);
        return true;
    }

    private void Prepay(EntityUid gun, CFRangedWeaponComponent ranged, bool prepaid)
    {
        if (!prepaid)
            return;

        ranged.ReloadPrepaid = true;
        Dirty(gun, ranged);
    }

    private bool TryHeld(EntityUid user, out EntityUid gun, out CFRangedWeaponComponent ranged)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp(held, out CFRangedWeaponComponent? shooter) && HasComp<ContainerAmmoProviderComponent>(held))
            {
                gun = held;
                ranged = shooter;
                return true;
            }
        }

        gun = default;
        ranged = default!;
        return false;
    }

    private bool TryFindRound(EntityUid user, ItemSlot slot, out EntityUid round)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (Fits(held, slot))
            {
                round = held;
                return true;
            }

            if (TryFindIn(held, slot, 1, out round))
                return true;
        }

        var worn = _inventory.GetSlotEnumerator(user);
        while (worn.MoveNext(out var container))
        {
            if (container.ContainedEntity is not { } item)
                continue;

            if (Fits(item, slot))
            {
                round = item;
                return true;
            }

            if (TryFindIn(item, slot, 1, out round))
                return true;
        }

        round = default;
        return false;
    }

    private bool TryFindIn(EntityUid item, ItemSlot slot, int depth, out EntityUid round)
    {
        round = default;
        if (depth > MaxDepth || !_containers.TryGetContainer(item, StorageComponent.ContainerId, out var storage))
            return false;

        foreach (var inside in storage.ContainedEntities)
        {
            if (Fits(inside, slot))
            {
                round = inside;
                return true;
            }

            if (TryFindIn(inside, slot, depth + 1, out round))
                return true;
        }

        return false;
    }

    private bool Fits(EntityUid item, ItemSlot slot)
    {
        return _whitelist.IsWhitelistPass(slot.Whitelist, item);
    }

    private void Perform(Entity<CFReloadActionComponent> ent, EntityUid user, EntityUid? target)
    {
        if (ent.Comp.Then is not { } then)
        {
            TryReload(user, prepaid: false);
            return;
        }

        if (!_maneuvers.CanUse(user, then, target))
            return;

        _maneuvers.TryUse(user, then, target);
        TryReload(user, prepaid: true);
    }

    [SubscribeLocalEvent]
    private void OnInstant(Entity<CFReloadActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_net.IsServer)
            Perform(ent, args.Performer, null);
    }

    [SubscribeLocalEvent]
    private void OnTarget(Entity<CFReloadActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_net.IsServer)
            Perform(ent, args.Performer, args.Target);
    }
}
