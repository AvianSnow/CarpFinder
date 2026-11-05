using Content.Client.CF.Character;
using Content.Shared.CF.Shop;
using Robust.Client.UserInterface;

namespace Content.Client.CF.Shop;

public sealed class CFMerchantBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private CFMerchantWindow? _window;
    private CFShopWindow? _shop;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CFMerchantWindow>();
        _window.SetCanBuy(EntMan.HasComponent<CFShopAccessComponent>(PlayerManager.LocalEntity));
        _window.OnSell += () => SendMessage(new CFMerchantSellMessage());
        _window.OnBuy += () =>
        {
            _shop ??= new CFShopWindow();
            _shop.OpenCentered();
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is CFMerchantState merchant)
            _window?.UpdateState(merchant);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _shop?.Close();
    }
}
