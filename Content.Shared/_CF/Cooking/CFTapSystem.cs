using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Shared.CF.Cooking;

/// <summary>
/// An empty mug, bottle or bucket used on something it can draw from is filled from it. Stock tries to pour first,
/// so an empty mug on a barrel with a tap only said the mug was empty, because a barrel can be filled too. CP14's mug
/// drew first through an injector. This keeps the stock drink transfer and only decides which way an empty one goes.
/// It answers the click on the thing being drawn from, before the stock transfer runs on the container: that handler
/// never looks at <c>Handled</c>, so it has to be kept from running at all, or it pours the drink straight back.
/// </summary>
public sealed partial class CFTapSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SolutionTransferSystem _transfer = default!;

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<DrainableSolutionComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var held = args.Used;
        if (!TryComp(held, out SolutionTransferComponent? transfer)
            || !transfer.CanReceive
            || !TryComp(held, out RefillableSolutionComponent? refillable)
            || !_solutions.TryGetRefillableSolution((held, refillable), out var into, out var heldSolution)
            || heldSolution.Volume > 0
            || !_solutions.TryGetDrainableSolution((ent.Owner, ent.Comp), out var from, out var source)
            || source.Volume == 0)
            return;

        var amount = transfer.TransferAmount;
        if (refillable.MaxRefill is { } maxRefill)
            amount = FixedPoint2.Min(amount, maxRefill);

        args.Handled = true;
        var time = refillable.RefillTime + ent.Comp.DrainTime;
        if (time > TimeSpan.Zero)
        {
            // Stock's own handler finishes it.
            _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, time, new SolutionRefillTransferDoAfterEvent(amount), held, ent)
            {
                BreakOnDamage = true,
                BreakOnMove = true,
                NeedHand = true,
                Hidden = true,
            });
            return;
        }

        var transferred = _transfer.Transfer(new SolutionTransferData(args.User, ent, from.Value, held, into.Value, amount));
        if (transferred <= 0)
            return;

        var key = heldSolution.AvailableVolume == 0 ? "comp-solution-transfer-fill-fully" : "comp-solution-transfer-fill-normal";
        _popup.PopupEntity(Loc.GetString(key, ("owner", ent.Owner), ("amount", transferred), ("target", held)), held, args.User);
    }
}
