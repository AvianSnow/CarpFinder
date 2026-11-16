using Content.Shared.CF.GOAP;
using Content.Shared.Nutrition.EntitySystems;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>Walks to the tribe's nearest water and drinks there until it has had enough.</summary>
public sealed partial class CFGOAPDrinkStepSystem : CFGOAPErrandStepSystem
{
    [Dependency] private IngestionSystem _ingestion = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Drink;

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        return TryNearest(errand, errand.Tribe.Comp.Waters, out target);
    }

    /// <summary>The nearest of the tribe's things of one kind. Water and fire are shared, so they are not claimed.</summary>
    public bool TryNearest(CFErrand errand, List<EntityUid> things, out EntityUid nearest)
    {
        nearest = default;
        var best = float.MaxValue;
        var here = Xform.GetMapCoordinates(errand.Uid).Position;
        foreach (var thing in things)
        {
            if (TerminatingOrDeleted(thing))
                continue;

            var distance = (Xform.GetMapCoordinates(thing).Position - here).Length();
            if (distance >= best)
                continue;

            best = distance;
            nearest = thing;
        }

        return best < float.MaxValue;
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        base.Begin(ent, action);

        // Any number can drink from one well.
        if (TryGetErrand(ent, out var errand) && errand.Social.Errand is { } water)
            Tribes.Release(errand.Tribe.Comp, water, ent);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        var tribe = errand.Tribe.Comp;
        if (Tribes.Share(errand.Uid, SatiationSystem.Thirst) is not { } share || share >= tribe.FullAbove)
            return true;

        var now = Timing.CurTime;
        if (now < errand.Social.NextBite)
            return false;

        errand.Social.NextBite = now + CFGOAPEatStepSystem.BiteInterval;
        _ingestion.TryIngest(errand.Uid, target);
        return false;
    }
}
