using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Shop;

/// <summary>Lets a character use the in-game shop. The merchant job grants it; everyone else buys from merchants.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFShopAccessComponent : Component;

/// <summary>A physical coin stack. One coin is worth <see cref="Copper"/>.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFCoinComponent : Component
{
    [DataField(required: true)]
    public int Copper = 1;
}

/// <summary>
/// A merchant's counter. Only a merchant works it. An adventurer places goods on it (<c>ItemPlacer</c>). The merchant
/// sees each one's market price, types their own price, and buys it: the coins come out of the merchant's wallet and
/// are laid on the counter for the adventurer to pick up. Bought goods are
/// stored in the counter, where the merchant takes them or sells them on to the Merchants' Guild at full worth.
/// Based on CP14's selling platform, without its factions, reputation and shifting prices.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFMerchantCounterComponent : Component
{
    public const string StockContainer = "cf_merchant_stock";

    [DataField]
    public SoundSpecifier SellSound = new SoundCollectionSpecifier("Coin");
}

/// <summary>Whether a guild pallet takes goods away or receives orders.</summary>
[Serializable, NetSerializable]
public enum CFGuildPalletKind : byte
{
    Sell,
    Buy,
}

/// <summary>
/// A Merchants' Guild pallet. Goods on a sell pallet are sold to the guild, which keeps <see cref="Cut"/> percent
/// for teleporting them away. Merchants are guild members and keep it all. A merchant standing by a buy pallet gets
/// their shop orders on it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFGuildPalletComponent : Component
{
    [DataField]
    public CFGuildPalletKind Kind = CFGuildPalletKind.Sell;

    [DataField]
    public int Cut = 50;

    [DataField]
    public SoundSpecifier SellSound = new SoundCollectionSpecifier("Coin");
}

[Serializable, NetSerializable]
public enum CFMerchantUiKey : byte
{
    Key,
}

/// <summary>Which guild pallet the trading window is open on.</summary>
[Serializable, NetSerializable]
public enum CFMerchantMode : byte
{
    GuildSell,
    GuildBuy,
}

/// <summary>Sells everything on a guild sell pallet.</summary>
[Serializable, NetSerializable]
public sealed class CFMerchantSellMessage : BoundUserInterfaceMessage;

/// <summary>The merchant buys one thing on the counter for <see cref="Copper"/>, the price they typed.</summary>
[Serializable, NetSerializable]
public sealed class CFCounterBuyMessage(NetEntity item, int copper) : BoundUserInterfaceMessage
{
    public readonly NetEntity Item = item;
    public readonly int Copper = copper;
}

/// <summary>The merchant takes one stored thing into their hands.</summary>
[Serializable, NetSerializable]
public sealed class CFCounterTakeMessage(NetEntity item) : BoundUserInterfaceMessage
{
    public readonly NetEntity Item = item;
}

/// <summary>The merchant sells one stored thing to the guild, or all of them when <see cref="Item"/> is null.</summary>
[Serializable, NetSerializable]
public sealed class CFCounterGuildSellMessage(NetEntity? item) : BoundUserInterfaceMessage
{
    public readonly NetEntity? Item = item;
}

/// <summary>
/// One thing on a counter or in its store. <see cref="Copper"/> is its market price, its full worth, 0 when nobody
/// buys it.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct CFCounterEntry(
    NetEntity Item,
    string Prototype,
    string Name,
    string Description,
    int Copper);

/// <summary>What is on a merchant's counter, and what it has stored.</summary>
[Serializable, NetSerializable]
public sealed class CFMerchantCounterState(List<CFCounterEntry> offers, List<CFCounterEntry> stored)
    : BoundUserInterfaceState
{
    public readonly List<CFCounterEntry> Offers = offers;
    public readonly List<CFCounterEntry> Stored = stored;
}

/// <summary>One thing on a pallet, at its full worth. Zero means nobody will take it.</summary>
[Serializable, NetSerializable]
public readonly record struct CFMerchantEntry(string Name, int Copper);

/// <summary>
/// What is on a guild pallet. Every viewer gets the same state, so the window works out what applies to its own
/// viewer: a merchant pays no guild cut.
/// </summary>
[Serializable, NetSerializable]
public sealed class CFMerchantState(List<CFMerchantEntry> entries, CFMerchantMode mode, int rate)
    : BoundUserInterfaceState
{
    public readonly List<CFMerchantEntry> Entries = entries;
    public readonly CFMerchantMode Mode = mode;

    /// <summary>The share of each entry's worth the guild pays someone who is not a merchant, in percent.</summary>
    public readonly int Rate = rate;

    /// <summary>The percent paid to this viewer.</summary>
    public int RateFor(bool merchant)
    {
        return merchant ? 100 : Rate;
    }

    /// <summary>What the goods fetch for this viewer, in copper.</summary>
    public int TotalFor(bool merchant)
    {
        var rate = RateFor(merchant);
        var total = 0;
        foreach (var entry in Entries)
        {
            total += CFCoins.Share(entry.Copper, rate);
        }

        return total;
    }
}
