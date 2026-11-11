using Robust.Shared.GameStates;

namespace Content.Shared.CF.Shop;

/// <summary>
/// A wallet. The coins inside the first one a character carries are their money: the shop, merchants and the guild
/// spend from it and pay into it. Coins anywhere else do not count until they are put in.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFWalletComponent : Component;
