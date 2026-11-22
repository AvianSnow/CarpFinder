using Content.Shared.CF.Combat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Items;

/// <summary>Sent by the HUD deck's Items tab. The item is checked on the server, because it comes from a client.</summary>
[Serializable, NetSerializable]
public sealed class CFDrawQuickItemEvent : EntityEventArgs
{
    public NetEntity Item;
}

/// <summary>
/// The consumables and thrown weapons a creature carries in its pockets, belt and bags, and taking one into a free hand.
/// </summary>
public sealed partial class CFQuickItemSystem : EntitySystem
{
    /// <summary>A bag in a bag in a bag is as far as anyone looks.</summary>
    private const int MaxDepth = 3;

    private static readonly ProtoId<TagPrototype>[] QuickTags = { "CFConsumable", "CFPotion", "CFSpellScroll" };

    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;

    public bool IsQuickItem(EntityUid item)
    {
        return _tags.HasAnyTag(item, QuickTags) || _weapons.ThrownIncrement(item) > 0;
    }

    /// <summary>Fills <paramref name="found"/> with what could be drawn. Held items are not included.</summary>
    public void Carried(EntityUid user, List<EntityUid> found)
    {
        found.Clear();
        if (!TryComp<InventoryComponent>(user, out var inventory))
            return;

        var slots = _inventory.GetSlotEnumerator((user, inventory));
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } item)
                Visit(item, found, 0);
        }
    }

    private void Visit(EntityUid item, List<EntityUid> found, int depth)
    {
        if (IsQuickItem(item))
            found.Add(item);

        if (depth >= MaxDepth || !_containers.TryGetContainer(item, StorageComponent.ContainerId, out var storage))
            return;

        foreach (var inside in storage.ContainedEntities)
            Visit(inside, found, depth + 1);
    }

    /// <summary>Moves a carried item to a free hand. It costs no action, the same as moving it there by hand.</summary>
    public bool TryDraw(EntityUid user, EntityUid item)
    {
        var carried = new List<EntityUid>();
        Carried(user, carried);
        if (!carried.Contains(item))
            return false;

        if (_hands.CountFreeHands(user) <= 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-hud-items-no-hand"), user, user);
            return false;
        }

        // A hand cannot take an item straight out of a worn slot, such as a pocket.
        if (_inventory.TryGetContainingSlot(item, out var slot)
            && !_inventory.TryUnequip(user, slot.Name, silent: true))
            return false;

        return _hands.TryPickupAnyHand(user, item, animate: false);
    }

    [SubscribeNetworkEvent]
    private void OnDraw(CFDrawQuickItemEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } user && TryGetEntity(ev.Item, out var item))
            TryDraw(user, item.Value);
    }
}
