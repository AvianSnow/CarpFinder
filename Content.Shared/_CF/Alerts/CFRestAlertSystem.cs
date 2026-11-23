using Content.Shared.Alert;
using Content.Shared.Bed.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Alerts;

/// <summary>
/// Sleep and Wake as buttons on the alerts panel, the column beside the temperature and pressure icons, in place of
/// the stock actions on the hotbar. A creature buckled to a bed sees Sleep, a sleeping one sees Asleep, and clicking
/// either does what the action did. Stock still gives the two actions, but <c>autoPopulate: false</c> on them keeps
/// them off the hotbar.
/// </summary>
public sealed partial class CFRestAlertSystem : EntitySystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private SleepingSystem _sleeping = default!;

    public static readonly ProtoId<AlertCategoryPrototype> Category = "CFRest";
    public static readonly ProtoId<AlertPrototype> SleepAlert = "CFSleep";
    public static readonly ProtoId<AlertPrototype> WakeAlert = "CFWake";

    [SubscribeLocalEvent]
    private void OnBuckled(Entity<AlertsComponent> ent, ref BuckledEvent args)
    {
        Refresh(ent, inBed: IsBed(args.Strap));
    }

    [SubscribeLocalEvent]
    private void OnUnbuckled(Entity<AlertsComponent> ent, ref UnbuckledEvent args)
    {
        Refresh(ent, inBed: false);
    }

    [SubscribeLocalEvent]
    private void OnSleepStateChanged(Entity<AlertsComponent> ent, ref SleepStateChangedEvent args)
    {
        Refresh(ent, asleep: args.FellAsleep);
    }

    [SubscribeLocalEvent]
    private void OnSleepAlert(Entity<AlertsComponent> ent, ref CFSleepAlertEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = _sleeping.TrySleeping(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnWakeAlert(Entity<AlertsComponent> ent, ref CFWakeAlertEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = _sleeping.TryWakeWithCooldown(ent.Owner);
    }

    /// <summary>A bed is whatever stock gives the Sleep action for: a strap with <see cref="HealOnBuckleComponent"/>.</summary>
    private bool IsBed(EntityUid strap) => HasComp<HealOnBuckleComponent>(strap);

    /// <summary>
    /// Shows the one button that fits. The state that is changing is passed in, because during the event that reports
    /// it the components still say the old thing: a sleeping component being removed is still there.
    /// </summary>
    private void Refresh(Entity<AlertsComponent> ent, bool? inBed = null, bool? asleep = null)
    {
        if (TerminatingOrDeleted(ent))
            return;

        asleep ??= HasComp<SleepingComponent>(ent);
        if (asleep.Value)
        {
            _alerts.ShowAlert(ent.Owner, WakeAlert);
            return;
        }

        inBed ??= TryComp(ent, out BuckleComponent? buckle) && buckle.BuckledTo is { } strap && IsBed(strap);
        if (inBed.Value)
            _alerts.ShowAlert(ent.Owner, SleepAlert);
        else
            _alerts.ClearAlertCategory(ent.Owner, Category);
    }
}

/// <summary>Clicking the Sleep button.</summary>
public sealed partial class CFSleepAlertEvent : BaseAlertEvent;

/// <summary>Clicking the Asleep button.</summary>
public sealed partial class CFWakeAlertEvent : BaseAlertEvent;
