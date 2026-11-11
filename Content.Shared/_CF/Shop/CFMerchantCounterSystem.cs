using Content.Shared.Popups;
using Content.Shared.UserInterface;

namespace Content.Shared.CF.Shop;

/// <summary>Only a merchant opens a merchant's counter. Everyone else places goods on it and waits.</summary>
public sealed partial class CFMerchantCounterSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    [SubscribeLocalEvent]
    private void OnOpenAttempt(Entity<CFMerchantCounterComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (HasComp<CFShopAccessComponent>(args.User))
            return;

        args.Cancel();
        if (!args.Silent)
            _popup.PopupEntity(Loc.GetString("cf-counter-not-merchant"), ent, args.User);
    }
}
