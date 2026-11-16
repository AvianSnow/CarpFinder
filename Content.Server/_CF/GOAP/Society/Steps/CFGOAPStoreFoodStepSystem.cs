using Content.Shared.CF.GOAP;
using Content.Shared.Storage.EntitySystems;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>Carries the food it holds home and puts it in a store with room, for the rest of the tribe to eat from.</summary>
public sealed partial class CFGOAPStoreFoodStepSystem : CFGOAPErrandStepSystem
{
    [Dependency] private SharedStorageSystem _storage = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.StoreFood;

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        target = default;
        if (!Tribes.TryHeldFood(errand.Uid, out var food))
            return false;

        var nearest = float.MaxValue;
        var here = Xform.GetMapCoordinates(errand.Uid).Position;
        foreach (var store in errand.Tribe.Comp.Stores)
        {
            if (TerminatingOrDeleted(store) || !_storage.CanInsert(store, food, out _))
                continue;

            var distance = (Xform.GetMapCoordinates(store).Position - here).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            target = store;
        }

        return nearest < float.MaxValue;
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        base.Begin(ent, action);
        if (TryGetErrand(ent, out var errand) && errand.Social.Errand is { } store)
            Tribes.Release(errand.Tribe.Comp, store, ent);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        if (Tribes.TryHeldFood(errand.Uid, out var food) && _storage.Insert(target, food, out _, user: errand.Uid))
            errand.Tribe.Comp.FoodStock++;

        return true;
    }
}
