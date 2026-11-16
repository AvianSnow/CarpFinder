using Content.Shared.CF.GOAP;
using Content.Shared.CF.Harvest;
using Content.Shared.Item;
using Content.Shared.Storage;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>
/// Picks up food that lies loose, or takes a piece out of one of the tribe's stores. Which store is chosen from what
/// the tribe knows is in each, not by opening them; only the one it goes to is opened, to take the piece out.
/// A carcass is loose food too: the member fetches the survival knife, butchers it where it lies, as a player does,
/// puts the knife down and picks up a piece of what came out. The rest lies there for the others.
/// </summary>
public sealed partial class CFGOAPTakeFoodStepSystem : CFGOAPErrandStepSystem
{
    /// <summary>Butchering is a stock do-after of a few seconds. Longer than this and something stopped it.</summary>
    private static readonly TimeSpan ButcherTime = TimeSpan.FromSeconds(12);

    private const float PartsReach = 1.5f;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private CFHarvestSystem _harvest = default!;

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
        var tribe = errand.Tribe.Comp;

        // Its own pack counts as a store of its own, and it is always nearest.
        if (Tribes.TryGetPack(errand.Uid, out var pack) && tribe.Stock.TryGetValue(pack, out var stash) && (stash.Food > 0 || stash.Ingredients > 0))
        {
            target = pack;
            return true;
        }

        foreach (var store in tribe.Stores)
        {
            if (!tribe.Stock.TryGetValue(store, out var record) || record.Food == 0 && record.Ingredients == 0)
                continue;

            var distance = (Xform.GetMapCoordinates(store).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            target = store;
        }

        // Nothing known. A hungry member goes and looks in a chest nobody has checked for a while.
        return nearest < float.MaxValue || Society.TryFindUncheckedStore(tribe, here, out target);
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (TryGetErrand(ent, out var before))
            before.Social.Acted = false;

        base.Begin(ent, action);

        // A store serves everyone at once. Only a loose piece of food, or a carcass, is one member's to fetch.
        if (action.FromStore && TryGetErrand(ent, out var errand) && errand.Social.Errand is { } store)
            Tribes.Release(errand.Tribe.Comp, store, ent);
    }

    /// <summary>A carcass wants the knife first.</summary>
    protected override EntityUid? PickTool(CFErrand errand, EntityUid target)
    {
        if (!Society.IsCarcass(target)
            || !Society.TryFindButcherKnife(errand.Uid, errand.Tribe.Comp, Xform.GetMapCoordinates(errand.Uid), out var knife))
            return null;

        return knife;
    }

    /// <summary>A piece in the store that can be eaten, or that the tribe can work on. Edible first.</summary>
    private bool TryFoodIn(CFErrand errand, EntityUid store, out EntityUid food)
    {
        food = default;
        if (TerminatingOrDeleted(store) || !TryComp(store, out StorageComponent? storage))
            return false;

        var here = Xform.GetMapCoordinates(errand.Uid);
        EntityUid? workable = null;
        foreach (var item in storage.Container.ContainedEntities)
        {
            var stage = Tribes.Stage(item);
            if (Tribes.IsEdible(item))
            {
                food = item;
                return true;
            }

            if (stage != CFFoodStage.None && workable == null && Society.TryFindStation(errand.Uid, errand.Tribe.Comp, here, item, out _))
                workable = item;
        }

        if (workable is not { } piece)
            return false;

        food = piece;
        return true;
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        if (!action.FromStore)
        {
            if (HasComp<CFHarvestableComponent>(target))
                return Butcher(errand, target);

            if (!Containers.IsEntityInContainer(target))
                Hands.TryPickupAnyHand(errand.Uid, target);

            return true;
        }

        // It has the chest open: what is in it is what the tribe knows from now on.
        var tribe = errand.Tribe.Comp;
        Tribes.LookInto(tribe, target);
        if (!TryFoodIn(errand, target, out var food))
            return true;

        if (Hands.TryPickupAnyHand(errand.Uid, food))
        {
            Tribes.NoteTaken(tribe, target, food);
            if (tribe.FoodStock < tribe.LowStock)
                Tribes.Remember(errand.Uid, Loc.GetString("cf-tribe-memory-stock-low"));
        }

        return true;
    }

    /// <summary>Butchers the carcass with the knife in hand, then puts the knife down and takes a piece of the meat.</summary>
    private bool Butcher(CFErrand errand, EntityUid carcass)
    {
        var uid = errand.Uid;
        var social = errand.Social;
        if (social.Tool is not { } knife || !Hands.IsHolding(uid, knife) || !TryComp(carcass, out CFHarvestableComponent? harvestable))
            return true;

        var now = Timing.CurTime;
        if (!social.Acted)
        {
            if (!_harvest.TryStartHarvest(uid, (carcass, harvestable), knife, requireTraining: false))
                return true;

            social.Acted = true;
            return false;
        }

        if (!TerminatingOrDeleted(carcass) && !harvestable.Harvested && now < (social.ArrivedAt ?? now) + ButcherTime)
            return false;

        Hands.TryDrop(uid, knife);

        var here = Xform.GetMapCoordinates(uid);
        var nearest = float.MaxValue;
        EntityUid? meat = null;
        foreach (var item in _lookup.GetEntitiesInRange<ItemComponent>(here, PartsReach))
        {
            if (Containers.IsEntityInContainer(item) || !Society.IsWanted(uid, errand.Tribe.Comp, here, item))
                continue;

            var distance = (Xform.GetMapCoordinates(item).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            meat = item;
        }

        if (meat is { } piece)
            Hands.TryPickupAnyHand(uid, piece);

        return true;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        // A knife still in hand when the plan changes goes back on the ground, so its hands are free.
        if (TryGetErrand(ent, out var before) && before.Social.Tool is { } knife && HasComp<CFHarvestToolComponent>(knife) && Hands.IsHolding(ent.Owner, knife))
            Hands.TryDrop(ent.Owner, knife);

        base.End(ent, action);
        if (TryGetErrand(ent, out var errand))
            errand.Social.Acted = false;
    }
}
