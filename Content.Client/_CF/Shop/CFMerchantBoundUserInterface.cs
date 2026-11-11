using Content.Client.CF.Character;
using Content.Shared.CF.Shop;
using Robust.Client.UserInterface;

namespace Content.Client.CF.Shop;

/// <summary>
/// The trading windows: a merchant's counter, with its store in a second window, or a Merchants' Guild pallet.
/// </summary>
public sealed class CFMerchantBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private CFMerchantWindow? _pallet;
    private CFCounterWindow? _counter;
    private CFCounterStoreWindow? _store;
    private CFShopWindow? _shop;
    private CFMerchantCounterState? _counterState;

    protected override void Open()
    {
        base.Open();

        if (EntMan.HasComponent<CFMerchantCounterComponent>(Owner))
        {
            _counter = this.CreateWindow<CFCounterWindow>();
            _counter.OnBuy += (item, copper) => SendMessage(new CFCounterBuyMessage(item, copper));
            _counter.OnStored += OpenStore;
            _counter.OnShop += OpenShop;
            return;
        }

        _pallet = this.CreateWindow<CFMerchantWindow>();
        _pallet.SetMerchant(EntMan.HasComponent<CFShopAccessComponent>(PlayerManager.LocalEntity));
        _pallet.OnSell += () => SendMessage(new CFMerchantSellMessage());
        _pallet.OnBuy += OpenShop;
    }

    private void OpenShop()
    {
        _shop ??= new CFShopWindow();
        _shop.OpenCentered();
    }

    private void OpenStore()
    {
        if (_store == null)
        {
            _store = new CFCounterStoreWindow();
            _store.OnTake += item => SendMessage(new CFCounterTakeMessage(item));
            _store.OnSell += item => SendMessage(new CFCounterGuildSellMessage(item));
        }

        if (_counterState != null)
            _store.UpdateState(_counterState);
        _store.OpenCentered();
    }

    private int Wallet()
    {
        return PlayerManager.LocalEntity is { } player ? EntMan.System<CFWalletSystem>().Total(player) : 0;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case CFMerchantCounterState counter:
                _counterState = counter;
                _counter?.UpdateState(counter, Wallet());
                _store?.UpdateState(counter);
                break;
            case CFMerchantState pallet:
                _pallet?.UpdateState(pallet);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _shop?.Close();
        _store?.Close();
    }
}
