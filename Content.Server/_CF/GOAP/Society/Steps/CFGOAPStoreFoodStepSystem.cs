using Content.Shared.CF.GOAP;
using Content.Shared.Storage.EntitySystems;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>
/// Carries the food it holds home and puts it in a store with room, for the rest of the tribe to eat from. The
/// tribe's count of what the store holds goes up with it, so nobody has to open the crate to know.
/// </summary>
public sealed partial class CFGOAPStoreFoodStepSystem : CFGOAPErrandStepSystem
{
    [Dependency] private SharedStorageSystem _storage = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.StoreFood;

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        target = default;
        if (!Tribes.TryHeldFood(errand.Uid, out var food))
            return false;

        // Food the tribe can still work on is worked on first. Ending here lets thinking plan that stage.
        var here = Xform.GetMapCoordinates(errand.Uid);
        if (Tribes.Stage(food) != CFFoodStage.Ready && Society.TryFindStation(errand.Uid, errand.Tribe.Comp, here, food, out _))
            return false;

        // With no store that has room, the pack on its own back.
        return Tribes.TryFindStore(errand.Tribe.Comp, food, here, out target, errand.Uid);
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        base.Begin(ent, action);

        // A store serves everyone at once.
        if (TryGetErrand(ent, out var errand) && errand.Social.Errand is { } store)
            Tribes.Release(errand.Tribe.Comp, store, ent);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        if (Tribes.TryHeldFood(errand.Uid, out var food) && _storage.Insert(target, food, out _, user: errand.Uid))
            Tribes.NoteStored(errand.Tribe.Comp, target, food);

        return true;
    }
}
