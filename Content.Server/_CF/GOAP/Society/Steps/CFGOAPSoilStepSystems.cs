using Content.Shared.Botany.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.CF.GOAP;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Stacks;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>
/// Walks to a plot nobody has looked at for a while and looks: how wet it is, how rich, and what grows there.
/// This is the only way the tribe learns any of that. What it finds wrong it remembers, and tells the others.
/// </summary>
public sealed partial class CFGOAPInspectSoilStepSystem : CFGOAPErrandStepSystem
{
    protected override CFGOAPStep Kind => CFGOAPStep.InspectSoil;

    protected override TimeSpan Dwell => TimeSpan.FromSeconds(1.5);

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        return Society.TryFindPlot(errand.Uid, errand.Tribe.Comp, Xform.GetMapCoordinates(errand.Uid), CFPlotNeed.Inspect, out target);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        var tribe = errand.Tribe.Comp;
        var record = Society.Look(tribe, target);
        if (record.Ready)
            Tribes.Remember(errand.Uid, Loc.GetString("cf-tribe-memory-soil-ready"));
        else if (record.Water < tribe.DryBelow)
            Tribes.Remember(errand.Uid, Loc.GetString("cf-tribe-memory-soil-dry"));
        else if (record.Nutrients < tribe.PoorBelow)
            Tribes.Remember(errand.Uid, Loc.GetString("cf-tribe-memory-soil-poor"));

        return true;
    }
}

/// <summary>
/// Takes a bucket, fills it at the tribe's water if it is empty, and pours it on a dry plot. The bucket is put down
/// by the plot afterwards, so its hands are free again.
/// </summary>
public sealed partial class CFGOAPWaterSoilStepSystem : CFGOAPErrandStepSystem
{
    /// <summary>Less than this in the bucket is not worth the walk to the plot.</summary>
    private static readonly FixedPoint2 WorthPouring = FixedPoint2.New(10);

    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private CFGOAPDrinkStepSystem _drink = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.WaterSoil;

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        return Society.TryFindPlot(errand.Uid, errand.Tribe.Comp, Xform.GetMapCoordinates(errand.Uid), CFPlotNeed.Water, out target);
    }

    protected override EntityUid? PickTool(CFErrand errand, EntityUid target)
    {
        return Society.TryFindBucket(errand.Uid, errand.Tribe.Comp, Xform.GetMapCoordinates(errand.Uid), out var bucket)
            ? bucket
            : null;
    }

    protected override EntityUid? Detour(CFErrand errand, EntityUid target)
    {
        if (errand.Social.Tool is not { } bucket
            || !_solutions.TryGetDrainableSolution(bucket, out _, out var held)
            || held.Volume >= WorthPouring)
            return null;

        return _drink.TryNearest(errand, errand.Tribe.Comp.Waters, out var water) ? water : null;
    }

    protected override void AtDetour(CFErrand errand, EntityUid detour)
    {
        if (errand.Social.Tool is not { } bucket
            || !_solutions.TryGetRefillableSolution(bucket, out var into, out var held)
            || !_solutions.TryGetDrainableSolution(detour, out var from, out _))
            return;

        var drawn = _solutions.Drain(detour, from.Value, held.AvailableVolume);
        _solutions.TryAddSolution(into.Value, drawn);

        // A dry well would otherwise hold it here for good.
        if (held.Volume < WorthPouring)
            errand.Social.Errand = null;
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        if (errand.Social.Tool is not { } bucket
            || !_solutions.TryGetDrainableSolution(bucket, out var from, out var held)
            || !_solutions.TryGetRefillableSolution(target, out var soil, out var ground))
            return true;

        var poured = _solutions.Drain(bucket, from.Value, FixedPoint2.Min(held.Volume, ground.AvailableVolume));
        _solutions.TryAddSolution(soil.Value, poured);
        Hands.TryDrop(errand.Uid, bucket);

        // The plot takes the water in over the next while. It knows it has just watered it, so it does not look dry.
        var record = Society.Look(errand.Tribe.Comp, target);
        record.Water = 1f;
        return true;
    }
}

/// <summary>Works fertilizer into a plot that was poor when last looked at.</summary>
public sealed partial class CFGOAPFeedSoilStepSystem : CFGOAPErrandStepSystem
{
    /// <summary>What one piece of fertilizer adds, out of a plot's 100.</summary>
    private const float Nutrients = 40f;

    [Dependency] private PlantTraySystem _trays = default!;
    [Dependency] private SharedStackSystem _stacks = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.FeedSoil;

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        return Society.TryFindPlot(errand.Uid, errand.Tribe.Comp, Xform.GetMapCoordinates(errand.Uid), CFPlotNeed.Feed, out target);
    }

    protected override EntityUid? PickTool(CFErrand errand, EntityUid target)
    {
        return Society.TryFindFertilizer(errand.Uid, Xform.GetMapCoordinates(errand.Uid), out var fertilizer) ? fertilizer : null;
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        if (errand.Social.Tool is not { } fertilizer || !Hands.IsHolding(errand.Uid, fertilizer))
            return true;

        _trays.AdjustNutrient(target, Nutrients);
        if (!TryComp(fertilizer, out StackComponent? stack) || !_stacks.TryUse((fertilizer, stack), 1))
            QueueDel(fertilizer);

        Society.Look(errand.Tribe.Comp, target);
        return true;
    }
}

/// <summary>
/// Takes a seed and plants it in a plot that was empty when last looked at, the way a player does. With no seed
/// about, it takes the clippers to a grown plant first and clips a seed from it, as a player does with stock's
/// sample taker, then plants that.
/// </summary>
public sealed partial class CFGOAPPlantSeedStepSystem : CFGOAPErrandStepSystem
{
    private const float SeedReach = 1.5f;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.PlantSeed;

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        return Society.TryFindPlot(errand.Uid, errand.Tribe.Comp, Xform.GetMapCoordinates(errand.Uid), CFPlotNeed.Plant, out target);
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (TryGetErrand(ent, out var before))
        {
            before.Social.Working = null;
            before.Social.Acted = false;
        }

        base.Begin(ent, action);
    }

    protected override EntityUid? PickTool(CFErrand errand, EntityUid target)
    {
        var here = Xform.GetMapCoordinates(errand.Uid);
        var tribe = errand.Tribe.Comp;
        if (Society.TryFindSeed(errand.Uid, tribe, here, out var seed))
            return seed;

        if (!Society.TryFindClippers(errand.Uid, tribe, here, out var clippers)
            || !Society.TryFindPlot(errand.Uid, tribe, here, CFPlotNeed.Sample, out var grown))
            return null;

        errand.Social.Working = grown;
        Tribes.Claim(tribe, grown, errand.Uid);
        return clippers;
    }

    protected override EntityUid? Detour(CFErrand errand, EntityUid target)
    {
        // Until a seed is in hand, the way goes past the grown plant.
        var social = errand.Social;
        if (social.Working is not { } grown || social.Tool is { } tool && HasComp<SeedComponent>(tool))
            return null;

        return grown;
    }

    protected override void AtDetour(CFErrand errand, EntityUid detour)
    {
        var uid = errand.Uid;
        var social = errand.Social;
        var tribe = errand.Tribe.Comp;
        if (!social.Acted)
        {
            if (social.Tool is { } clippers && Hands.IsHolding(uid, clippers))
            {
                if (TryComp(detour, out PlantTrayComponent? tray) && tray.PlantEntity is { } plant && !TerminatingOrDeleted(plant))
                    _interaction.InteractUsing(uid, clippers, plant, Transform(plant).Coordinates, checkCanInteract: false, checkCanUse: false);

                Hands.TryDrop(uid, clippers);
                Tribes.Release(tribe, clippers, uid);
            }

            social.Tool = null;
            social.Acted = true;
            Society.Look(tribe, detour);
            return;
        }

        // The seed packet fell at its feet, if the plant gave one.
        var here = Xform.GetMapCoordinates(uid);
        EntityUid? found = null;
        var nearest = float.MaxValue;
        foreach (var seed in _lookup.GetEntitiesInRange<SeedComponent>(here, SeedReach))
        {
            if (Containers.IsEntityInContainer(seed))
                continue;

            var distance = (Xform.GetMapCoordinates(seed).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            found = seed;
        }

        Tribes.Release(tribe, detour, uid);
        social.Working = null;
        if (found is { } packet && Hands.TryPickupAnyHand(uid, packet))
        {
            social.Tool = packet;
            Tribes.Claim(tribe, packet, uid);
            return;
        }

        // Too young to clip, or the clippers missed. Nothing to plant this time.
        social.Errand = null;
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        if (errand.Social.Tool is { } seed && HasComp<SeedComponent>(seed) && Hands.IsHolding(errand.Uid, seed))
            _interaction.InteractUsing(errand.Uid, seed, target, Transform(target).Coordinates, checkCanInteract: false, checkCanUse: false);

        Society.Look(errand.Tribe.Comp, target);
        return true;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        base.End(ent, action);
        if (!TryGetErrand(ent, out var errand))
            return;

        if (errand.Social.Working is { } grown)
            Tribes.Release(errand.Tribe.Comp, grown, ent);

        errand.Social.Working = null;
        errand.Social.Acted = false;
    }
}

/// <summary>Picks a ripe crop by hand, or pulls up a dead plant. What it picks falls at its feet, for the next errand.</summary>
public sealed partial class CFGOAPHarvestStepSystem : CFGOAPErrandStepSystem
{
    [Dependency] private SharedInteractionSystem _interaction = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Harvest;

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        return Society.TryFindPlot(errand.Uid, errand.Tribe.Comp, Xform.GetMapCoordinates(errand.Uid), CFPlotNeed.Harvest, out target);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        if (TryComp(target, out PlantTrayComponent? tray) && tray.PlantEntity is { } plant && !TerminatingOrDeleted(plant))
            _interaction.InteractHand(errand.Uid, plant);

        Society.Look(errand.Tribe.Comp, target);
        return true;
    }
}
