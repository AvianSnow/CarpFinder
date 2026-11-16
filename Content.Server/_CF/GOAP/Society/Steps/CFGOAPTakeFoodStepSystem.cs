using Content.Shared.CF.GOAP;
using Content.Shared.Storage;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>Picks up food that lies loose, or takes a piece out of one of the tribe's stores.</summary>
public sealed partial class CFGOAPTakeFoodStepSystem : CFGOAPErrandStepSystem
{
    protected override CFGOAPStep Kind => CFGOAPStep.TakeFood;

    protected override float Reach => 1.4f;

    protected override TimeSpan Dwell => TimeSpan.FromSeconds(0.5);

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        var here = Xform.GetMapCoordinates(errand.Uid);
        if (!action.FromStore)
            return Society.TryFindLooseFood(errand.Uid, errand.Tribe.Comp, here, out target);

        target = default;
        var nearest = float.MaxValue;
        foreach (var store in errand.Tribe.Comp.Stores)
        {
            if (!TryFoodIn(store, out _))
                continue;

            var distance = (Xform.GetMapCoordinates(store).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            target = store;
        }

        return nearest < float.MaxValue;
    }

    private bool TryFoodIn(EntityUid store, out EntityUid food)
    {
        food = default;
        if (TerminatingOrDeleted(store) || !TryComp(store, out StorageComponent? storage))
            return false;

        foreach (var item in storage.Container.ContainedEntities)
        {
            if (!Tribes.IsFood(item))
                continue;

            food = item;
            return true;
        }

        return false;
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        base.Begin(ent, action);

        // A store serves everyone at once. Only a loose piece of food is one member's to fetch.
        if (action.FromStore && TryGetErrand(ent, out var errand) && errand.Social.Errand is { } store)
            Tribes.Release(errand.Tribe.Comp, store, ent);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        if (!action.FromStore)
        {
            if (!Containers.IsEntityInContainer(target))
                Hands.TryPickupAnyHand(errand.Uid, target);

            return true;
        }

        if (TryFoodIn(target, out var food) && Hands.TryPickupAnyHand(errand.Uid, food))
        {
            var tribe = errand.Tribe.Comp;
            tribe.FoodStock = Math.Max(0, tribe.FoodStock - 1);
            if (tribe.FoodStock < tribe.LowStock)
                Tribes.Remember(errand.Uid, Loc.GetString("cf-tribe-memory-stock-low"));
        }

        return true;
    }
}
