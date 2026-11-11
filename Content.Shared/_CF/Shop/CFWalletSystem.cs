using System.Diagnostics.CodeAnalysis;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Content.Shared.Storage;

namespace Content.Shared.CF.Shop;

/// <summary>
/// Finds a character's wallet and counts the coins in it. Both sides run it, so the shop window can show the money.
/// Spending and paying spawn coins, so they are on the server (<c>CFCoinSystem</c>).
/// </summary>
public sealed partial class CFWalletSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;

    /// <summary>
    /// The wallet the character carries: held, worn, or in a pocket first, then inside a bag they carry.
    /// </summary>
    public bool TryGetWallet(EntityUid user, [NotNullWhen(true)] out EntityUid? wallet)
    {
        wallet = null;
        foreach (var item in _inventory.GetHandOrInventoryEntities(user))
        {
            if (!HasComp<CFWalletComponent>(item))
                continue;

            wallet = item;
            return true;
        }

        foreach (var item in _inventory.GetHandOrInventoryEntities(user))
        {
            if (!TryComp(item, out StorageComponent? storage))
                continue;

            foreach (var inside in storage.Container.ContainedEntities)
            {
                if (!HasComp<CFWalletComponent>(inside))
                    continue;

                wallet = inside;
                return true;
            }
        }

        return false;
    }

    /// <summary>The coins in the character's wallet, in copper. 0 with no wallet.</summary>
    public int Total(EntityUid user)
    {
        return TryGetWallet(user, out var wallet) ? Contents(wallet.Value) : 0;
    }

    /// <summary>The coins in one wallet, in copper.</summary>
    public int Contents(EntityUid wallet)
    {
        if (!TryComp(wallet, out StorageComponent? storage))
            return 0;

        var total = 0;
        foreach (var item in storage.Container.ContainedEntities)
        {
            total += CoinValue(item);
        }

        return total;
    }

    /// <summary>What a coin stack is worth, in copper. 0 for anything that is not coins.</summary>
    public int CoinValue(EntityUid uid)
    {
        if (!TryComp(uid, out CFCoinComponent? coin))
            return 0;

        var count = TryComp(uid, out StackComponent? stack) ? stack.Count : 1;
        return coin.Copper * count;
    }
}
