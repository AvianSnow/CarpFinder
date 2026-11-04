using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Items;

/// <summary>
/// Adds up the Bulk a creature carries (hands, worn items, and everything inside them),
/// marks it encumbered, and refuses pickups past its maximum.
/// </summary>
public sealed partial class CFEncumbranceSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        // Every combatant carries things. The component is added the first time it is counted.
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFCombatantComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            var comp = EnsureComp<CFEncumbranceComponent>(uid);
            if (comp.NextCheck > now)
                continue;

            comp.NextCheck = now + CheckInterval;
            Refresh((uid, comp));
        }
    }

    /// <summary>Recounts the creature's Bulk now. Call after a large change, such as a loadout.</summary>
    public void Refresh(Entity<CFEncumbranceComponent> ent)
    {
        var tenths = CarriedTenths(ent);
        var encumbered = CFBulk.IsEncumbered(tenths, Strength(ent));
        if (tenths == ent.Comp.CarriedTenths && encumbered == ent.Comp.Encumbered)
            return;

        var changed = encumbered != ent.Comp.Encumbered;
        ent.Comp.CarriedTenths = tenths;
        ent.Comp.Encumbered = encumbered;
        Dirty(ent);

        if (!changed)
            return;

        _speed.RefreshMovementSpeedModifiers(ent.Owner);
        _popup.PopupEntity(Loc.GetString(encumbered ? "cf-bulk-encumbered" : "cf-bulk-unencumbered"), ent, ent);
    }

    public bool IsEncumbered(EntityUid uid)
    {
        return TryComp(uid, out CFEncumbranceComponent? comp) && comp.Encumbered;
    }

    /// <summary>The Bulk of one item, not counting what is inside it.</summary>
    public int ItemTenths(EntityUid item)
    {
        if (TryComp(item, out CFBulkComponent? bulk))
            return CFBulk.ToTenths(bulk.Bulk);

        return TryComp(item, out ItemComponent? stock) ? CFBulk.FromItemSize(stock.Size.Id) : 0;
    }

    /// <summary>Everything in the creature's hands and inventory slots, and inside those, in tenths of Bulk.</summary>
    public int CarriedTenths(EntityUid uid)
    {
        var total = 0;
        foreach (var item in _inventory.GetHandOrInventoryEntities(uid))
        {
            total += ItemAndContentsTenths(item);
        }

        return total;
    }

    private int ItemAndContentsTenths(EntityUid item)
    {
        var total = ItemTenths(item);
        if (!TryComp(item, out ContainerManagerComponent? manager))
            return total;

        foreach (var container in _containers.GetAllContainers(item, manager))
        {
            foreach (var inner in container.ContainedEntities)
            {
                total += ItemAndContentsTenths(inner);
            }
        }

        return total;
    }

    [SubscribeLocalEvent]
    private void OnPickupAttempt(EntityUid uid, CFEncumbranceComponent comp, PickupAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var after = comp.CarriedTenths + ItemAndContentsTenths(args.Item);
        if (!CFBulk.IsOverMaximum(after, Strength(uid)))
            return;

        args.Cancel();
        if (args.ShowPopup && _timing.IsFirstTimePredicted)
            _popup.PopupClient(Loc.GetString("cf-bulk-too-heavy"), uid, uid);
    }

    private int Strength(EntityUid uid)
    {
        return TryComp(uid, out CFAbilityScoreComponent? abilities) ? abilities.GetModifier(CFAbility.Strength) : 0;
    }
}
