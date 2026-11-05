using Content.Server.Stack;
using Content.Shared.CF.Shop;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.Shop;

/// <summary>
/// Moves money between the purse and physical coins. Pocketing coins fills the purse.
/// Taking coins out spends it, so they can be handed over, dropped or stored.
/// </summary>
public sealed partial class CFCoinSystem : EntitySystem
{
    private static readonly VerbCategory TakeCategory = new("cf-coins-take", null);

    private static readonly (ProtoId<StackPrototype> Stack, int Copper, string Name)[] Coins =
    {
        ("CFPlatinumCoin", CFCoins.CopperPerPlatinum, "pp"),
        ("CFGoldCoin", CFCoins.CopperPerGold, "gp"),
        ("CFSilverCoin", CFCoins.CopperPerSilver, "sp"),
        ("CFCopperCoin", 1, "cp"),
    };

    private static readonly int[] Amounts = { 1, 10 };

    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private CFShopSystem _shop = default!;

    [SubscribeLocalEvent]
    private void OnCoinUsed(Entity<CFCoinComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var value = Pocket(ent, args.User);
        _popup.PopupEntity(Loc.GetString("cf-coins-pocketed", ("amount", CFCoins.FormatPrice(value))), args.User, args.User);
    }

    /// <summary>Adds the whole stack to the purse and removes it. Returns its value in copper.</summary>
    public int Pocket(Entity<CFCoinComponent> coins, EntityUid user)
    {
        var count = CompOrNull<StackComponent>(coins)?.Count ?? 1;
        var value = coins.Comp.Copper * count;

        var purse = _shop.Purse(user);
        purse.Copper += value;
        Dirty(user, purse);
        Del(coins);
        return value;
    }

    /// <summary>Takes coins out of the purse into a hand, or next to the user. Null if the purse is too light.</summary>
    public EntityUid? TakeOut(EntityUid user, ProtoId<StackPrototype> stack, int coinValue, int amount)
    {
        var purse = _shop.Purse(user);
        var cost = coinValue * amount;
        if (purse.Copper < cost)
            return null;

        purse.Copper -= cost;
        Dirty(user, purse);

        var coins = _stacks.SpawnNextToOrDrop(amount, stack, user);
        _hands.TryPickupAnyHand(user, coins, checkActionBlocker: false);
        return coins;
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<CFPurseComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (args.User != args.Target || !args.CanInteract)
            return;

        var user = args.User;
        var purse = _shop.Purse(user);
        var priority = 0;
        foreach (var (stack, copper, name) in Coins)
        {
            foreach (var amount in Amounts)
            {
                var pickedStack = stack;
                var pickedCopper = copper;
                var pickedAmount = amount;
                args.Verbs.Add(new Verb
                {
                    Text = $"{amount} {name}",
                    Category = TakeCategory,
                    Priority = priority--,
                    Disabled = purse.Copper < copper * amount,
                    Act = () => TakeOut(user, pickedStack, pickedCopper, pickedAmount),
                });
            }
        }
    }
}
