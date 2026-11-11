using Content.Shared.CF.Harvest;
using Content.Shared.CF.Shop;
using Content.Shared.Item;
using Content.Shared.Placeable;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server.CF.Shop;

/// <summary>
/// Selling, for PF2e prices. At a merchant's counter an adventurer offers goods, and the merchant buys them at a price
/// they set, laying the coins on the counter, and can sell them on to the guild at full worth. On a guild sell pallet anyone sells to the Merchants'
/// Guild, which keeps half, except from merchants, who are members.
/// </summary>
public sealed partial class CFMerchantSystem : EntitySystem
{
    /// <summary>How far a pallet looks for goods lying on it.</summary>
    private const float PalletReach = 0.45f;

    /// <summary>How often an open pallet window is refreshed, since nothing says when goods are dropped on it.</summary>
    private static readonly TimeSpan PalletRefresh = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private CFShopSystem _shop = default!;
    [Dependency] private CFCoinSystem _coins = default!;

    private TimeSpan _nextRefresh;

    /// <summary>
    /// The full worth of this item: its Price, or its share of the Price for part of a stack. 0 if nobody buys it.
    /// </summary>
    public int Worth(EntityUid item)
    {
        if (MetaData(item).EntityPrototype is not { } prototype)
            return 0;

        // Nobody buys a bag with things still in it: they would be lost with it.
        if (TryComp(item, out StorageComponent? storage) && storage.Container.ContainedEntities.Count > 0)
            return 0;

        // Coins are money, not goods.
        if (HasComp<CFCoinComponent>(item))
            return 0;

        // A harvested part is worth what its monster and the harvest made it, not its prototype's price.
        if (TryComp(item, out CFPartsValueComponent? part))
            return part.CopperPerUnit * (CompOrNull<StackComponent>(item)?.Count ?? 1);

        var price = CFCoins.EntityPrice(prototype);
        if (price <= 0)
            return 0;

        var count = 1;
        var prototypeCount = 1;
        if (TryComp(item, out StackComponent? stack))
        {
            count = stack.Count;
            prototypeCount = prototype.TryComp(out StackComponent? printed, Factory) ? printed.Count : 1;
        }

        return CFCoins.Worth(price, count, prototypeCount);
    }

    /// <summary>Merchants are guild members: they run counters and pay the guild nothing.</summary>
    public bool IsMerchant(EntityUid user)
    {
        return HasComp<CFShopAccessComponent>(user);
    }

    /// <summary>The share of an item's worth the guild pays <paramref name="user"/>, in percent.</summary>
    public int GuildRate(Entity<CFGuildPalletComponent> pallet, EntityUid? user)
    {
        return user != null && IsMerchant(user.Value) ? 100 : 100 - pallet.Comp.Cut;
    }

    #region Counter

    /// <summary>The goods on the counter for the merchant to buy. Coins lying there are payment, not goods.</summary>
    public List<EntityUid> Placed(EntityUid counter)
    {
        var placed = new List<EntityUid>();
        if (!TryComp(counter, out ItemPlacerComponent? placer))
            return placed;

        foreach (var item in placer.PlacedEntities)
        {
            if (!TerminatingOrDeleted(item) && !HasComp<CFCoinComponent>(item))
                placed.Add(item);
        }

        return placed;
    }

    /// <summary>What the counter has bought and holds for the merchant.</summary>
    public List<EntityUid> Stored(EntityUid counter)
    {
        return _container.TryGetContainer(counter, CFMerchantCounterComponent.StockContainer, out var stock)
            ? new List<EntityUid>(stock.ContainedEntities)
            : new List<EntityUid>();
    }

    /// <summary>
    /// The merchant buys one thing on the counter for <paramref name="price"/>, the price they set, or the market
    /// price when null. The coins come out of the merchant's wallet and are laid on the counter for the adventurer to
    /// pick up, and the thing goes into the counter's store.
    /// </summary>
    public bool TryBuy(Entity<CFMerchantCounterComponent> counter, EntityUid merchant, EntityUid item, int? price = null)
    {
        if (!IsMerchant(merchant) || !Placed(counter).Contains(item))
            return false;

        if (Worth(item) <= 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-counter-not-wanted"), merchant, merchant);
            return false;
        }

        var paid = Math.Max(0, price ?? Worth(item));
        if (!_coins.TrySpend(merchant, paid))
        {
            _popup.PopupEntity(Loc.GetString("cf-shop-short"), merchant, merchant);
            return false;
        }

        var stock = _container.EnsureContainer<Container>(counter, CFMerchantCounterComponent.StockContainer);
        _container.Insert(item, stock);
        _coins.PayOnto(counter, paid);

        _audio.PlayPvs(counter.Comp.SellSound, counter);
        _popup.PopupEntity(Loc.GetString("cf-counter-bought",
            ("item", item), ("amount", CFCoins.FormatPrice(paid))), counter);
        return true;
    }

    /// <summary>The merchant takes one stored thing into their hands, or the bag, or onto the floor.</summary>
    public bool TryTake(Entity<CFMerchantCounterComponent> counter, EntityUid merchant, EntityUid item)
    {
        if (!IsMerchant(merchant)
            || !_container.TryGetContainer(counter, CFMerchantCounterComponent.StockContainer, out var stock)
            || !stock.Contains(item))
            return false;

        _container.Remove(item, stock);
        _shop.Give(merchant, item);
        return true;
    }

    /// <summary>
    /// The merchant sells stored goods to the Merchants' Guild at full worth, as a member: one thing, or all when
    /// <paramref name="item"/> is null. Goods nobody buys stay. Returns what was paid.
    /// </summary>
    public int SellStored(Entity<CFMerchantCounterComponent> counter, EntityUid merchant, EntityUid? item)
    {
        if (!IsMerchant(merchant))
            return 0;

        var total = 0;
        foreach (var good in Stored(counter))
        {
            if (item != null && good != item)
                continue;

            var worth = Worth(good);
            if (worth <= 0)
                continue;

            total += worth;
            Del(good);
        }

        if (total <= 0)
            return 0;

        _coins.Pay(merchant, total);
        _audio.PlayPvs(counter.Comp.SellSound, counter);
        _popup.PopupEntity(Loc.GetString("cf-guild-sold", ("amount", CFCoins.FormatPrice(total))), merchant, merchant);
        return total;
    }

    [SubscribeLocalEvent]
    private void OnCounterBuy(Entity<CFMerchantCounterComponent> ent, ref CFCounterBuyMessage args)
    {
        if (TryGetEntity(args.Item, out var item))
            TryBuy(ent, args.Actor, item.Value, args.Copper);
        SendState(ent);
    }

    [SubscribeLocalEvent]
    private void OnCounterTake(Entity<CFMerchantCounterComponent> ent, ref CFCounterTakeMessage args)
    {
        if (TryGetEntity(args.Item, out var item))
            TryTake(ent, args.Actor, item.Value);
        SendState(ent);
    }

    [SubscribeLocalEvent]
    private void OnCounterGuildSell(Entity<CFMerchantCounterComponent> ent, ref CFCounterGuildSellMessage args)
    {
        EntityUid? item = null;
        if (args.Item is { } net)
        {
            if (!TryGetEntity(net, out item))
                return;
        }

        SellStored(ent, args.Actor, item);
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

    private CFCounterEntry Entry(EntityUid item)
    {
        var meta = MetaData(item);
        return new CFCounterEntry(GetNetEntity(item), meta.EntityPrototype?.ID ?? string.Empty, meta.EntityName,
            meta.EntityDescription, Worth(item));
    }

    private void SendState(Entity<CFMerchantCounterComponent> ent)
    {
        var offers = new List<CFCounterEntry>();
        foreach (var item in Placed(ent))
        {
            offers.Add(Entry(item));
        }

        var stored = new List<CFCounterEntry>();
        foreach (var item in Stored(ent))
        {
            stored.Add(Entry(item));
        }

        _ui.SetUiState(ent.Owner, CFMerchantUiKey.Key, new CFMerchantCounterState(offers, stored));
    }

    #endregion

    #region Guild pallets

    /// <summary>Loose goods lying on a pallet's tile.</summary>
    public List<EntityUid> OnPallet(EntityUid pallet)
    {
        var goods = new List<EntityUid>();
        foreach (var uid in _lookup.GetEntitiesInRange(Transform(pallet).Coordinates, PalletReach, LookupFlags.Uncontained))
        {
            if (uid == pallet || !HasComp<ItemComponent>(uid) || Transform(uid).Anchored)
                continue;

            goods.Add(uid);
        }

        return goods;
    }

    /// <summary>
    /// Sells everything on a guild sell pallet that has a value, paying <paramref name="user"/>'s wallet.
    /// The guild keeps its cut unless the seller is a merchant. Returns what was paid.
    /// </summary>
    public int SellToGuild(Entity<CFGuildPalletComponent> pallet, EntityUid user)
    {
        if (pallet.Comp.Kind != CFGuildPalletKind.Sell)
            return 0;

        var rate = GuildRate(pallet, user);
        var total = 0;
        foreach (var item in OnPallet(pallet))
        {
            var value = CFCoins.Share(Worth(item), rate);
            if (value <= 0)
                continue;

            total += value;
            Del(item);
        }

        if (total <= 0)
            return 0;

        _coins.Pay(user, total);
        _audio.PlayPvs(pallet.Comp.SellSound, pallet);
        _popup.PopupEntity(Loc.GetString("cf-guild-sold", ("amount", CFCoins.FormatPrice(total))), user, user);
        return total;
    }

    [SubscribeLocalEvent]
    private void OnGuildSell(Entity<CFGuildPalletComponent> ent, ref CFMerchantSellMessage args)
    {
        SellToGuild(ent, args.Actor);
        SendPalletState(ent);
    }

    [SubscribeLocalEvent]
    private void OnPalletUiOpen(Entity<CFGuildPalletComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        SendPalletState(ent);
    }

    private void SendPalletState(Entity<CFGuildPalletComponent> ent)
    {
        var mode = ent.Comp.Kind == CFGuildPalletKind.Sell ? CFMerchantMode.GuildSell : CFMerchantMode.GuildBuy;
        var entries = new List<CFMerchantEntry>();
        if (ent.Comp.Kind == CFGuildPalletKind.Sell)
        {
            foreach (var item in OnPallet(ent))
            {
                entries.Add(new CFMerchantEntry(Name(item), Worth(item)));
            }
        }

        _ui.SetUiState(ent.Owner, CFMerchantUiKey.Key, new CFMerchantState(entries, mode, 100 - ent.Comp.Cut));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextRefresh)
            return;

        _nextRefresh = now + PalletRefresh;
        var query = EntityQueryEnumerator<CFGuildPalletComponent>();
        while (query.MoveNext(out var uid, out var pallet))
        {
            if (pallet.Kind == CFGuildPalletKind.Sell && _ui.IsUiOpen(uid, CFMerchantUiKey.Key))
                SendPalletState((uid, pallet));
        }
    }

    #endregion
}
