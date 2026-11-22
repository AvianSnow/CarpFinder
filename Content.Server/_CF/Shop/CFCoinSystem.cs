using Content.Server.Stack;
using Content.Shared.CF.Shop;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Server.CF.Shop;

/// <summary>
/// Real coins in a wallet. Spending takes the coins out and puts the change back. Paying adds coins to the wallet,
/// or puts them in a hand or on the floor when there is no room.
/// </summary>
public sealed partial class CFCoinSystem : EntitySystem
{
    [Dependency] private CFWalletSystem _wallets = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private StackSystem _stacks = default!;

    /// <summary>Using coins in hand puts them in the wallet.</summary>
    [SubscribeLocalEvent]
    private void OnCoinUsed(Entity<CFCoinComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_wallets.TryGetWallet(args.User, out var wallet))
        {
            _popup.PopupEntity(Loc.GetString("cf-wallet-none"), args.User, args.User);
            return;
        }

        var value = _wallets.CoinValue(ent);
        if (!Store(wallet.Value, ent, args.User))
        {
            _popup.PopupEntity(Loc.GetString("cf-wallet-full"), args.User, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString("cf-coins-pocketed", ("amount", CFCoins.FormatPrice(value))), args.User, args.User);
    }

    /// <summary>The coins in the character's wallet, in copper.</summary>
    public int Total(EntityUid user)
    {
        return _wallets.Total(user);
    }

    /// <summary>
    /// Takes <paramref name="copper"/> out of the character's wallet, breaking coins and putting the change back.
    /// False, and nothing taken, when the wallet holds too little.
    /// </summary>
    public bool TrySpend(EntityUid user, int copper)
    {
        if (copper <= 0)
            return true;

        if (!_wallets.TryGetWallet(user, out var wallet) || !TryComp(wallet, out StorageComponent? storage))
            return false;

        var held = _wallets.Contents(wallet.Value);
        if (held < copper)
            return false;

        foreach (var item in new List<EntityUid>(storage.Container.ContainedEntities))
        {
            if (HasComp<CFCoinComponent>(item))
                Del(item);
        }

        PayInto(wallet.Value, user, held - copper);
        return true;
    }

    /// <summary>Adds <paramref name="copper"/> in coins to the character's wallet, or gives them to the character.</summary>
    public void Pay(EntityUid user, int copper)
    {
        if (copper <= 0)
            return;

        if (_wallets.TryGetWallet(user, out var wallet))
        {
            PayInto(wallet.Value, user, copper);
            return;
        }

        foreach (var coins in SpawnCoins(copper, user))
        {
            _hands.TryPickupAnyHand(user, coins, checkActionBlocker: false);
        }
    }

    /// <summary>
    /// Puts <paramref name="copper"/> in coins into <paramref name="wallet"/>. What does not fit goes to
    /// <paramref name="holder"/>'s hands or the floor, so no money is ever lost.
    /// </summary>
    public void PayInto(EntityUid wallet, EntityUid? holder, int copper)
    {
        if (copper <= 0)
            return;

        foreach (var coins in SpawnCoins(copper, holder ?? wallet))
        {
            if (Store(wallet, coins, holder))
                continue;

            if (holder != null)
                _hands.TryPickupAnyHand(holder.Value, coins, checkActionBlocker: false);
        }
    }

    /// <summary>Lays <paramref name="copper"/> in coins on <paramref name="place"/>, such as a counter.</summary>
    public List<EntityUid> PayOnto(EntityUid place, int copper)
    {
        return copper > 0 ? SpawnCoins(copper, place) : new List<EntityUid>();
    }

    private bool Store(EntityUid wallet, EntityUid coins, EntityUid? user)
    {
        if (!TryComp(wallet, out StorageComponent? storage))
            return false;

        return _storage.Insert(wallet, coins, out _, user, storage, playSound: false);
    }

    private List<EntityUid> SpawnCoins(int copper, EntityUid near)
    {
        var spawned = new List<EntityUid>();
        var coords = Transform(near).Coordinates;
        if (_container.TryGetOuterContainer(near, Transform(near), out var outer))
            coords = Transform(outer.Owner).Coordinates;

        foreach (var (stack, count) in CFCoins.Breakdown(copper))
        {
            spawned.AddRange(_stacks.SpawnMultipleAtPosition(stack, count, coords));
        }

        return spawned;
    }
}
