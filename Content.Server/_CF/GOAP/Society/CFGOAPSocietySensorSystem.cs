using Content.Server.CF.GOAP.Sensors;
using Content.Server.CF.GOAP.Steering;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Items;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.Temperature.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Society;

/// <summary>What a plot needs, as far as the tribe knows from when it last looked.</summary>
public enum CFPlotNeed : byte
{
    Inspect,
    Water,
    Feed,
    Plant,
    Harvest,
}

/// <summary>
/// What a tribe member knows about its needs, its tribe's food and plots, and the strangers in front of it. The
/// errand steps use the same lookups to act, so what the sensor promised is what the step finds.
/// A plot's state is never read from the plot here: only from what was seen at the last look.
/// </summary>
public sealed partial class CFGOAPSocietySensorSystem : EntitySystem
{
    /// <summary>How far it spots food, seeds, buckets and tribemates.</summary>
    public const float SightTiles = 10f;

    private static readonly ProtoId<TagPrototype> RawTag = "Raw";

    private static readonly HashSet<string> LifeGoals = new() { "ShareFood", "Socialize", "StockFood", "TendCrops" };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private PlantHolderSystem _plants = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private CFGOAPItemSensorSystem _items = default!;
    [Dependency] private CFGOAPSteeringSystem _steering = default!;
    [Dependency] private CFGOAPTribeSystem _tribes = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        var goap = args.Creature.Comp;
        if (!TryComp(uid, out CFGOAPSocialComponent? social) || !_tribes.TryGetTribe(uid, out var tribe))
            return;

        var data = tribe.Comp;
        var was = (CFGOAPFact) goap.Facts;
        var busy = args.Has(CFGOAPFact.TargetVisible);

        if (busy && goap.Target is { } target && !HasComp<CFGOAPSocialComponent>(target)
            && _tribes.Meet(tribe, uid, target) == CFTribeStance.Parley)
            args.Facts |= CFGOAPFact.Parleying;

        // A goal the language model picked never outranks an enemy in sight.
        if (busy && goap.ForcedGoal is { } forced && LifeGoals.Contains(forced))
            goap.ForcedGoal = null;

        if (Needs(uid, SatiationSystem.Hunger, (was & CFGOAPFact.Hungry) != 0, data))
            args.Facts |= CFGOAPFact.Hungry;

        if (data.Waters.Count > 0 && Needs(uid, SatiationSystem.Thirst, (was & CFGOAPFact.Thirsty) != 0, data))
            args.Facts |= CFGOAPFact.Thirsty;

        var freeHand = _hands.CountFreeHands(uid) > 0;
        if (_tribes.TryHeldFood(uid, out var food))
        {
            args.Facts |= CFGOAPFact.HasFood;
            if (IsRaw(food) && _tribes.TryGetFire(data, out _))
                args.Facts |= CFGOAPFact.FoodRaw;

            if (!args.Has(CFGOAPFact.Hungry) && TryFindHungryMate(uid, social, tribe, args.Here, out _))
                args.Facts |= CFGOAPFact.MateHungry;
        }
        else if (freeHand)
        {
            if (TryFindLooseFood(uid, data, args.Here, out _))
                args.Facts |= CFGOAPFact.LooseFood;

            if (data.FoodStock > 0)
                args.Facts |= CFGOAPFact.StoredFood;
        }

        if (TryFindPlot(uid, data, args.Here, CFPlotNeed.Inspect, out _))
            args.Facts |= CFGOAPFact.SoilUnchecked;

        if (TryFindPlot(uid, data, args.Here, CFPlotNeed.Harvest, out _))
            args.Facts |= CFGOAPFact.CropReady;

        if (data.Waters.Count > 0
            && TryFindPlot(uid, data, args.Here, CFPlotNeed.Water, out _)
            && TryFindBucket(uid, data, args.Here, out _))
            args.Facts |= CFGOAPFact.SoilDry;

        if (TryFindPlot(uid, data, args.Here, CFPlotNeed.Feed, out _) && TryFindFertilizer(uid, args.Here, out _))
            args.Facts |= CFGOAPFact.SoilPoor;

        if (TryFindPlot(uid, data, args.Here, CFPlotNeed.Plant, out _) && TryFindSeed(uid, data, args.Here, out _))
            args.Facts |= CFGOAPFact.CanPlant;

        if (!busy && _timing.CurTime >= social.NextChat && TryFindChatMate(uid, social, tribe, args.Here, out _))
            args.Facts |= CFGOAPFact.WantsChat;
    }

    /// <summary>Below the tribe's mark it needs something, and once it has started it goes on until it is full.</summary>
    private bool Needs(EntityUid uid, ProtoId<Content.Shared.Nutrition.Prototypes.SatiationTypePrototype> type, bool already, CFGOAPTribeComponent tribe)
    {
        return _tribes.Share(uid, type) is { } share && share < (already ? tribe.FullAbove : tribe.NeedBelow);
    }

    public bool IsRaw(EntityUid food)
    {
        return _tags.HasTag(food, RawTag) && HasComp<TemperatureComponent>(food);
    }

    /// <summary>Writes down what a plot looks like now. Only called by a member standing at it.</summary>
    public CFSoilRecord Look(CFGOAPTribeComponent tribe, EntityUid plot)
    {
        var record = new CFSoilRecord { At = _timing.CurTime };
        if (TryComp(plot, out PlantTrayComponent? tray))
        {
            record.Water = tray.MaxWaterLevel > 0f ? tray.WaterLevel / tray.MaxWaterLevel : 1f;
            record.Nutrients = tray.MaxNutritionLevel > 0f ? tray.NutritionLevel / tray.MaxNutritionLevel : 1f;
            if (tray.PlantEntity is { } plant && !TerminatingOrDeleted(plant))
            {
                record.Planted = true;
                record.Ready = _plants.IsDead(plant)
                               || TryComp(plant, out PlantHolderComponent? holder) && holder.ReadyForHarvest;
            }
        }

        tribe.Soil[plot] = record;
        return record;
    }

    /// <summary>The nearest plot that needs this, going by what was last seen, and that nobody else is on their way to.</summary>
    public bool TryFindPlot(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, CFPlotNeed need, out EntityUid plot)
    {
        plot = default;
        var nearest = float.MaxValue;
        var now = _timing.CurTime;
        foreach (var candidate in tribe.Plots)
        {
            if (TerminatingOrDeleted(candidate) || !_tribes.IsFree(tribe, candidate, uid))
                continue;

            var known = tribe.Soil.TryGetValue(candidate, out var record) && now < record.At + tribe.InspectEvery;
            var wanted = need switch
            {
                CFPlotNeed.Inspect => !known,
                CFPlotNeed.Water => known && record!.Water < tribe.DryBelow,
                CFPlotNeed.Feed => known && record!.Nutrients < tribe.PoorBelow,
                CFPlotNeed.Plant => known && !record!.Planted,
                CFPlotNeed.Harvest => known && record!.Ready,
                _ => false,
            };
            if (!wanted)
                continue;

            var distance = (_transform.GetMapCoordinates(candidate).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            plot = candidate;
        }

        return nearest < float.MaxValue;
    }

    /// <summary>Food lying in sight that nobody else is going for.</summary>
    public bool TryFindLooseFood(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid food)
    {
        return TryFindLoose<ItemComponent>(uid, tribe, here, _tribes.IsFood, out food);
    }

    /// <summary>A bucket in hand, or one lying in sight.</summary>
    public bool TryFindBucket(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid bucket)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!IsBucket(held))
                continue;

            bucket = held;
            return true;
        }

        bucket = default;
        return _hands.CountFreeHands(uid) > 0 && TryFindLoose<RefillableSolutionComponent>(uid, tribe, here, IsBucket, out bucket);
    }

    private bool IsBucket(EntityUid uid)
    {
        return HasComp<ItemComponent>(uid)
               && HasComp<RefillableSolutionComponent>(uid)
               && _solutions.TryGetDrainableSolution(uid, out _, out _);
    }

    /// <summary>A stock seed in hand, or one lying in sight.</summary>
    public bool TryFindSeed(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid seed)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!HasComp<SeedComponent>(held))
                continue;

            seed = held;
            return true;
        }

        seed = default;
        return _hands.CountFreeHands(uid) > 0 && TryFindLoose<SeedComponent>(uid, tribe, here, null, out seed);
    }

    public bool TryFindFertilizer(EntityUid uid, MapCoordinates here, out EntityUid fertilizer)
    {
        return _items.TryFindHeld(uid, CFItemUse.Fertilizer, out fertilizer)
               || _hands.CountFreeHands(uid) > 0 && _items.TryFindItem(uid, here, CFItemUse.Fertilizer, out fertilizer);
    }

    private bool TryFindLoose<T>(
        EntityUid uid,
        CFGOAPTribeComponent tribe,
        MapCoordinates here,
        Func<EntityUid, bool>? filter,
        out EntityUid item)
        where T : IComponent
    {
        item = default;
        var nearest = float.MaxValue;
        foreach (var candidate in _lookup.GetEntitiesInRange<T>(here, SightTiles))
        {
            var found = candidate.Owner;
            if (_containers.IsEntityInContainer(found)
                || filter != null && !filter(found)
                || !_tribes.IsFree(tribe, found, uid))
                continue;

            var at = _transform.GetMapCoordinates(found);
            if (_steering.IsDangerous(at))
                continue;

            var distance = (at.Position - here.Position).Length();
            if (distance >= nearest || !_examine.InRangeUnOccluded(uid, found, SightTiles))
                continue;

            nearest = distance;
            item = found;
        }

        return nearest < float.MaxValue;
    }

    /// <summary>A tribemate in sight who has said it is hungry and holds nothing to eat. The one a language model named comes first.</summary>
    public bool TryFindHungryMate(
        EntityUid uid,
        CFGOAPSocialComponent social,
        Entity<CFGOAPTribeComponent> tribe,
        MapCoordinates here,
        out EntityUid mate)
    {
        return TryFindMate(uid, social, tribe, here, IsHungry, out mate);
    }

    private bool IsHungry(EntityUid mate)
    {
        return TryComp(mate, out CFGOAPComponent? goap)
               && CFGOAPSystem.Knows(goap, CFGOAPFact.Hungry)
               && !_tribes.TryHeldFood(mate, out _);
    }

    /// <summary>A tribemate in sight to talk to.</summary>
    public bool TryFindChatMate(
        EntityUid uid,
        CFGOAPSocialComponent social,
        Entity<CFGOAPTribeComponent> tribe,
        MapCoordinates here,
        out EntityUid mate)
    {
        return TryFindMate(uid, social, tribe, here, null, out mate);
    }

    private bool TryFindMate(
        EntityUid uid,
        CFGOAPSocialComponent social,
        Entity<CFGOAPTribeComponent> tribe,
        MapCoordinates here,
        Func<EntityUid, bool>? filter,
        out EntityUid mate)
    {
        mate = default;
        var nearest = float.MaxValue;
        foreach (var candidate in _tribes.Members(tribe))
        {
            if (candidate == uid || filter != null && !filter(candidate) || !_tribes.IsFree(tribe.Comp, candidate, uid))
                continue;

            var at = _transform.GetMapCoordinates(candidate);
            var distance = (at.Position - here.Position).Length();
            if (at.MapId != here.MapId || distance > SightTiles || !_examine.InRangeUnOccluded(uid, candidate, SightTiles))
                continue;

            // Whoever it was told to go to wins over whoever is nearest.
            if (candidate == social.Preferred)
            {
                mate = candidate;
                return true;
            }

            if (distance >= nearest)
                continue;

            nearest = distance;
            mate = candidate;
        }

        return nearest < float.MaxValue;
    }
}
