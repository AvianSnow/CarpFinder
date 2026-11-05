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
/// A merchant's counter. Goods placed on it (<c>ItemPlacer</c>) can be sold, and its window opens the shop.
/// Based on CP14's selling platform, without its factions, reputation and shifting prices.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFMerchantCounterComponent : Component
{
    [DataField]
    public SoundSpecifier SellSound = new SoundCollectionSpecifier("Coin");
}

[Serializable, NetSerializable]
public enum CFMerchantUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class CFMerchantSellMessage : BoundUserInterfaceMessage;

/// <summary>One thing on the counter. Zero value means the merchant will not take it.</summary>
[Serializable, NetSerializable]
public readonly record struct CFMerchantEntry(string Name, int Copper);

[Serializable, NetSerializable]
public sealed class CFMerchantState(List<CFMerchantEntry> entries, int total) : BoundUserInterfaceState
{
    public readonly List<CFMerchantEntry> Entries = entries;
    public readonly int Total = total;
}
