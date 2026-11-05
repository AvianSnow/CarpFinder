using Content.Shared.CF.Shop;
using Content.Shared.Placeable;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;

namespace Content.Server.CF.Shop;

/// <summary>Selling at a merchant's counter, for PF2e prices.</summary>
public sealed partial class CFMerchantSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private CFShopSystem _shop = default!;

    /// <summary>What the merchant pays for this item, or 0 if they will not take it.</summary>
    public int SaleValue(EntityUid item)
    {
        if (MetaData(item).EntityPrototype is not { } prototype)
            return 0;

        // Nobody buys a bag with things still in it: they would be lost with it.
        if (TryComp(item, out StorageComponent? storage) && storage.Container.ContainedEntities.Count > 0)
            return 0;

        var price = CFCoins.EntityPrice(prototype);
        if (price <= 0)
            return 0;

        var fullResale = prototype.TryComp(out CFItemPriceComponent? data, Factory) && data.FullResale;
        var count = 1;
        var prototypeCount = 1;
        if (TryComp(item, out StackComponent? stack))
        {
            count = stack.Count;
            prototypeCount = prototype.TryComp(out StackComponent? printed, Factory) ? printed.Count : 1;
        }

        return CFCoins.SaleValue(price, fullResale, count, prototypeCount);
    }

    private HashSet<EntityUid> Placed(EntityUid counter)
    {
        return TryComp(counter, out ItemPlacerComponent? placer)
            ? new HashSet<EntityUid>(placer.PlacedEntities)
            : new HashSet<EntityUid>();
    }

    /// <summary>Sells everything on the counter that has a value, paying <paramref name="user"/>'s purse.</summary>
    public int Sell(Entity<CFMerchantCounterComponent> counter, EntityUid user)
    {
        var total = 0;
        foreach (var item in Placed(counter))
        {
            var value = SaleValue(item);
            if (value <= 0)
                continue;

            total += value;
            Del(item);
        }

        if (total <= 0)
            return 0;

        var purse = _shop.Purse(user);
        purse.Copper += total;
        Dirty(user, purse);

        _audio.PlayPvs(counter.Comp.SellSound, counter);
        _popup.PopupEntity(Loc.GetString("cf-merchant-sold", ("amount", CFCoins.FormatPrice(total))), user, user);
        return total;
    }

    [SubscribeLocalEvent]
    private void OnSell(Entity<CFMerchantCounterComponent> ent, ref CFMerchantSellMessage args)
    {
        Sell(ent, args.Actor);
        SendState(ent);
    }

    [SubscribeLocalEvent]
    private void OnBeforeUiOpen(Entity<CFMerchantCounterComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        SendState(ent);
    }

    [SubscribeLocalEvent]
    private void OnItemPlaced(Entity<CFMerchantCounterComponent> ent, ref ItemPlacedEvent args)
    {
        SendState(ent);
    }

    [SubscribeLocalEvent]
    private void OnItemRemoved(Entity<CFMerchantCounterComponent> ent, ref ItemRemovedEvent args)
    {
        SendState(ent);
    }

    private void SendState(Entity<CFMerchantCounterComponent> ent)
    {
        var entries = new List<CFMerchantEntry>();
        var total = 0;
        foreach (var item in Placed(ent))
        {
            if (TerminatingOrDeleted(item))
                continue;

            var value = SaleValue(item);
            total += value;
            entries.Add(new CFMerchantEntry(Name(item), value));
        }

        _ui.SetUiState(ent.Owner, CFMerchantUiKey.Key, new CFMerchantState(entries, total));
    }
}
