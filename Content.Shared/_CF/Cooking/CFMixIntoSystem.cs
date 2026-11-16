using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Shared.CF.Cooking;

/// <summary>Mixing an ingredient into a liquid: flour used on water with enough in it becomes dough.</summary>
public sealed partial class CFMixIntoSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private CFCookingSystem _cooking = default!;

    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<CFMixIntoComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasEnough(ent, target))
            return;

        args.Handled = true;
        if (_net.IsClient)
            return;

        TryMix(ent, target, args.User);
    }

    /// <summary>Whether this holds enough of the liquid the ingredient wants.</summary>
    public bool HasEnough(Entity<CFMixIntoComponent> ent, EntityUid target)
    {
        return _solutions.TryGetDrainableSolution(target, out _, out var solution)
               && solution.GetTotalPrototypeQuantity(ent.Comp.Reagent) >= ent.Comp.Amount;
    }

    /// <summary>Draws the liquid and swaps the ingredient for its result, where the ingredient is.</summary>
    public bool TryMix(Entity<CFMixIntoComponent> ent, EntityUid target, EntityUid? user)
    {
        if (!_solutions.TryGetDrainableSolution(target, out var soln, out var solution)
            || solution.GetTotalPrototypeQuantity(ent.Comp.Reagent) < ent.Comp.Amount)
            return false;

        _solutions.RemoveReagent(soln.Value, ent.Comp.Reagent.Id, ent.Comp.Amount);
        var result = ent.Comp.Result;
        _cooking.Replace(ent, result);
        if (user is { } who)
            _popup.PopupEntity(Loc.GetString("cf-cooking-mixed"), who, who);

        return true;
    }
}
