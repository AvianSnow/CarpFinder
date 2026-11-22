using Content.Server.CF.GOAP.Sensors;
using Content.Server.CF.GOAP.Steering;
using Content.Server.CF.Temperature;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Botany.Traits.Components;
using Content.Shared.CF.Cooking;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Harvest;
using Content.Shared.CF.Items;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
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

    /// <summary>A grown plant the clippers can take a seed from.</summary>
    Sample,
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

    private static readonly HashSet<string> LifeGoals = new() { "ShareFood", "Socialize", "StockFood", "TendCrops", "KeepNest" };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private PlantHolderSystem _plants = default!;
    [Dependency] private PlantSystem _plant = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private CFMixIntoSystem _mix = default!;
    [Dependency] private CFHarvestSystem _harvest = default!;
    [Dependency] private CFFireplaceSystem _fireplaces = default!;
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

        // A hand holding the tool of the errand it is on, such as the knife for a carcass, is as good as free, or the
        // plan would change the moment it picked the tool up.
        var freeHand = _hands.CountFreeHands(uid) > 0 || social.Tool is { } tool && _hands.IsHolding(uid, tool);

        // A loaf put down to be cut is still its food, or the plan would change under the knife.
        var hasFood = _tribes.TryHeldFood(uid, out var food)
                      || social.Working is { } working && !TerminatingOrDeleted(working) && _tribes.IsFood(working) && (food = working) == working;
        if (hasFood && IsWanted(uid, data, args.Here, food))
        {
            args.Facts |= CFGOAPFact.HasFood;
            if (_tribes.Stage(food) != CFFoodStage.Ready)
                args.Facts |= CFGOAPFact.FoodRaw;

            if (!args.Has(CFGOAPFact.Hungry) && TryFindHungryMate(uid, social, tribe, args.Here, out _))
                args.Facts |= CFGOAPFact.MateHungry;
        }
        else if (freeHand)
        {
            if (TryFindLooseFood(uid, data, args.Here, out _))
                args.Facts |= CFGOAPFact.LooseFood;

            // Known food, known ingredients it can work on, or, when hungry, a chest nobody has looked into for a while.
            if (data.FoodStock > 0
                || StoredIngredients(data) > 0 && HasAnyStation(data)
                || args.Has(CFGOAPFact.Hungry) && TryFindUncheckedStore(data, args.Here, out _))
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

        if (TryFindPlot(uid, data, args.Here, CFPlotNeed.Plant, out _) && (TryFindSeed(uid, data, args.Here, out _) || CanClipSeed(uid, data, args.Here)))
            args.Facts |= CFGOAPFact.CanPlant;

        if (TryFindChore(uid, data, args.Here, out _, out _))
            args.Facts |= CFGOAPFact.NestUntended;

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

    /// <summary>Food worth picking up: it can be eaten as it is, or the tribe has what its next step needs.</summary>
    public bool IsWanted(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, EntityUid food)
    {
        return _tribes.IsEdible(food) || TryFindStation(uid, tribe, here, food, out _);
    }

    /// <summary>
    /// Where the next step with this food is done: the mortar for wheat, water for flour, the fire for dough or raw
    /// meat, the knife for a loaf. False when the food is ready, is not food, or the tribe lacks the thing.
    /// </summary>
    public bool TryFindStation(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, EntityUid food, out EntityUid station)
    {
        station = default;
        switch (_tribes.Stage(food))
        {
            case CFFoodStage.Grind:
                return TryNearestFree(uid, tribe, here, tribe.Mortars, null, out station);
            case CFFoodStage.Mix:
                if (!TryComp(food, out CFMixIntoComponent? mix))
                    return false;

                var ingredient = new Entity<CFMixIntoComponent>(food, mix);
                return TryNearestFree(uid, tribe, here, tribe.Waters, water => _mix.HasEnough(ingredient, water), out station)
                       || TryFindLoose<RefillableSolutionComponent>(uid, tribe, here, bucket => IsBucket(bucket) && _mix.HasEnough(ingredient, bucket), out station, out _);
            case CFFoodStage.Cook:
                return _tribes.TryGetFire(tribe, out station);
            case CFFoodStage.Slice:
                return TryNearestFree(uid, tribe, here, tribe.Knives, null, out station);
            default:
                return false;
        }
    }

    /// <summary>The nearest store nobody has looked into, or the survey counted, for <c>InspectEvery</c>.</summary>
    public bool TryFindUncheckedStore(CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid store)
    {
        var now = _timing.CurTime;
        return TryFindStoreWith(tribe, here, record => now >= record.At + tribe.InspectEvery, out store);
    }

    private bool HasAnyStation(CFGOAPTribeComponent tribe)
    {
        return tribe.Mortars.Count > 0 || tribe.Waters.Count > 0 || tribe.Knives.Count > 0 || _tribes.TryGetFire(tribe, out _);
    }

    private static int StoredIngredients(CFGOAPTribeComponent tribe)
    {
        var count = 0;
        foreach (var record in tribe.Stock.Values)
        {
            count += record.Ingredients;
        }

        return count;
    }

    private bool TryNearestFree(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, List<EntityUid> candidates, Func<EntityUid, bool>? filter, out EntityUid found)
    {
        found = default;
        var nearest = float.MaxValue;
        foreach (var candidate in candidates)
        {
            // One in its own hand is the nearest of all. One in anyone else's is spoken for.
            if (TerminatingOrDeleted(candidate)
                || _containers.IsEntityInContainer(candidate) && !_hands.IsHolding(uid, candidate)
                || !_tribes.IsFree(tribe, candidate, uid)
                || filter != null && !filter(candidate))
                continue;

            var at = _transform.GetMapCoordinates(candidate);
            var distance = (at.Position - here.Position).Length();
            if (at.MapId != here.MapId || distance >= nearest)
                continue;

            nearest = distance;
            found = candidate;
        }

        return nearest < float.MaxValue;
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
                var dead = _plants.IsDead(plant);
                var ready = TryComp(plant, out PlantHolderComponent? holder) && holder.ReadyForHarvest;
                record.Ready = dead || ready;
                record.Sampleable = !dead
                                    && !HasComp<PlantTraitSampledComponent>(plant)
                                    && TryComp(plant, out PlantComponent? growth)
                                    && (ready || _plant.GetGrowthStageValue((plant, growth)) > 1);
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
                CFPlotNeed.Sample => known && record!.Sampleable && !record.Ready,
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

    /// <summary>
    /// Food lying in sight that nobody else is going for, and that the tribe can eat or work on. A carcass counts,
    /// when the tribe has a knife to butcher it with: its meat is the food.
    /// </summary>
    public bool TryFindLooseFood(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid food)
    {
        var found = TryFindLoose<ItemComponent>(uid, tribe, here, item => IsWanted(uid, tribe, here, item), out food, out var distance);
        if (!TryFindButcherKnife(uid, tribe, here, out _)
            || !TryFindLoose<CFHarvestableComponent>(uid, tribe, here, IsCarcass, out var carcass, out var nearer)
            || found && nearer >= distance)
            return found;

        food = carcass;
        return true;
    }

    /// <summary>A dead animal with its parts still in it.</summary>
    public bool IsCarcass(EntityUid uid)
    {
        return TryComp(uid, out CFHarvestableComponent? harvestable) && _harvest.IsHarvestable((uid, harvestable));
    }

    /// <summary>A survival knife in hand, or lying about the nest.</summary>
    public bool TryFindButcherKnife(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid knife)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!HasComp<CFHarvestToolComponent>(held))
                continue;

            knife = held;
            return true;
        }

        return TryNearestFree(uid, tribe, here, tribe.Butchers, null, out knife);
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
        return _hands.CountFreeHands(uid) > 0 && TryFindLoose<RefillableSolutionComponent>(uid, tribe, here, IsBucket, out bucket, out _);
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
        return _hands.CountFreeHands(uid) > 0 && TryFindLoose<SeedComponent>(uid, tribe, here, null, out seed, out _);
    }

    /// <summary>With no seed about, a grown plant and the clippers give one.</summary>
    public bool CanClipSeed(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here)
    {
        return _hands.CountFreeHands(uid) > 0
               && TryFindClippers(uid, tribe, here, out _)
               && TryFindPlot(uid, tribe, here, CFPlotNeed.Sample, out _);
    }

    /// <summary>Clippers in hand, or lying about the nest.</summary>
    public bool TryFindClippers(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid clippers)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!HasComp<BotanySampleTakerComponent>(held))
                continue;

            clippers = held;
            return true;
        }

        return TryNearestFree(uid, tribe, here, tribe.Clippers, null, out clippers);
    }

    public bool TryFindFertilizer(EntityUid uid, MapCoordinates here, out EntityUid fertilizer)
    {
        return _items.TryFindHeld(uid, CFItemUse.Fertilizer, out fertilizer)
               || _hands.CountFreeHands(uid) > 0 && _items.TryFindItem(uid, here, CFItemUse.Fertilizer, out fertilizer);
    }

    /// <summary>Fuel in hand, or lying in sight with a hand free for it.</summary>
    public bool TryFindFuel(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid fuel)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!_tribes.IsFuel(held))
                continue;

            fuel = held;
            return true;
        }

        fuel = default;
        return _hands.CountFreeHands(uid) > 0 && TryFindLoose<ItemComponent>(uid, tribe, here, _tribes.IsFuel, out fuel, out _);
    }

    /// <summary>The nearest store whose record passes, as far as the tribe knows.</summary>
    public bool TryFindStoreWith(CFGOAPTribeComponent tribe, MapCoordinates here, Func<CFStoreRecord, bool> wanted, out EntityUid store)
    {
        store = default;
        var nearest = float.MaxValue;
        foreach (var (candidate, record) in tribe.Stock)
        {
            if (TerminatingOrDeleted(candidate) || !wanted(record))
                continue;

            var distance = (_transform.GetMapCoordinates(candidate).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            store = candidate;
        }

        return nearest < float.MaxValue;
    }

    /// <summary>
    /// The tribe's fire wants feeding: it is out, it is nearly through what it has, or a plot is poor and no ash lies
    /// about, so the fire is stoked to make some. Only when there is fuel to hand, in sight or in a store.
    /// </summary>
    public bool TryFindHungryFire(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid fire)
    {
        fire = default;
        var haveFuel = TryFindFuel(uid, tribe, here, out _) || TryFindStoreWith(tribe, here, record => record.Fuel > 0, out _);
        if (!haveFuel)
            return false;

        var forAsh = TryFindPlot(uid, tribe, here, CFPlotNeed.Feed, out _) && !TryFindFertilizer(uid, here, out _);
        var nearest = float.MaxValue;
        foreach (var candidate in tribe.Fires)
        {
            if (TerminatingOrDeleted(candidate) || !TryComp(candidate, out CFFireplaceComponent? fireplace))
                continue;

            var hearth = new Entity<CFFireplaceComponent>(candidate, fireplace);
            if (!_fireplaces.NeedsFuel(hearth, tribe.LowFuel) && !(forAsh && _fireplaces.IsBurning(candidate) && _fireplaces.HasRoom(hearth)))
                continue;

            var distance = (_transform.GetMapCoordinates(candidate).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            fire = candidate;
        }

        return nearest < float.MaxValue;
    }

    /// <summary>
    /// Something about the nest to see to, nearest first: a fire wanting fuel, rubbish or a puddle within the nest,
    /// a coin or a potion lying loose with a store to put it in, or something in its own hand that nobody can use.
    /// </summary>
    public bool TryFindChore(EntityUid uid, CFGOAPTribeComponent tribe, MapCoordinates here, out EntityUid chore, out CFChoreKind kind)
    {
        chore = default;
        kind = CFChoreKind.None;

        // The fire before anything. A member with fuel already in hand goes straight to it.
        if (TryFindHungryFire(uid, tribe, here, out var fire))
        {
            chore = fire;
            kind = CFChoreKind.FeedFire;
            return true;
        }

        // Its own hands next: put down what it cannot use, before anything else.
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (_tribes.IsRubbish(held))
            {
                chore = held;
                kind = CFChoreKind.Rubbish;
                return true;
            }

            if (_tribes.IsValuable(held) || _tribes.IsFuel(held) || _tribes.IsFood(held) && !IsWanted(uid, tribe, here, held))
            {
                chore = held;
                kind = CFChoreKind.Valuable;
                return true;
            }
        }

        if (tribe.Nest is not { } nestCoords)
            return false;

        var nest = _transform.ToMapCoordinates(nestCoords);
        if (nest.MapId != here.MapId)
            return false;

        bool InNest(EntityUid thing)
        {
            return (_transform.GetMapCoordinates(thing).Position - nest.Position).Length() <= tribe.NestTiles;
        }

        var nearest = float.MaxValue;
        if (TryFindLoose<PuddleComponent>(uid, tribe, here, InNest, out var puddle, out var distance))
        {
            chore = puddle;
            kind = CFChoreKind.Puddle;
            nearest = distance;
        }

        if (_hands.CountFreeHands(uid) == 0)
            return nearest < float.MaxValue;

        if (TryFindLoose<ItemComponent>(uid, tribe, here, item => InNest(item) && _tribes.IsRubbish(item), out var rubbish, out distance)
            && distance < nearest)
        {
            chore = rubbish;
            kind = CFChoreKind.Rubbish;
            nearest = distance;
        }

        if (tribe.Stores.Count > 0
            && TryFindLoose<ItemComponent>(uid, tribe, here, item => InNest(item) && _tribes.IsValuable(item) && _tribes.TryFindStore(tribe, item, here, out _), out var valuable, out distance)
            && distance < nearest)
        {
            chore = valuable;
            kind = CFChoreKind.Valuable;
            nearest = distance;
        }

        return nearest < float.MaxValue;
    }

    private bool TryFindLoose<T>(
        EntityUid uid,
        CFGOAPTribeComponent tribe,
        MapCoordinates here,
        Func<EntityUid, bool>? filter,
        out EntityUid item,
        out float nearest)
        where T : IComponent
    {
        item = default;
        nearest = float.MaxValue;
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
