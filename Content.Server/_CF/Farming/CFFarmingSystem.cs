using Content.Server.CF.Outdoors;
using Content.Shared.CF.Farming;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.EntityTable;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.Farming;

/// <summary>
/// Planting seeds in soil, growing them on sunlight and water, and picking the crop.
/// Based on CP14's <c>CP14FarmingSystem</c>.
/// </summary>
public sealed partial class CFFarmingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private EntityTableSystem _tables = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private CFOutdoorsSystem _outdoors = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFPlantComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdateTime = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(ent.Comp.UpdateFrequency));
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var plants = EntityQueryEnumerator<CFPlantComponent>();
        while (plants.MoveNext(out var uid, out var plant))
        {
            if (now <= plant.NextUpdateTime)
                continue;

            plant.NextUpdateTime = now + TimeSpan.FromSeconds(_random.NextFloat(plant.UpdateFrequency));
            Grow((uid, plant));
        }
    }

    /// <summary>One update: take in sun and water, then grow if there is enough of both.</summary>
    public void Grow(Entity<CFPlantComponent> ent)
    {
        var plant = ent.Comp;

        if (_outdoors.UnderSunlight(ent))
            plant.Energy = Math.Min(plant.EnergyMax, plant.Energy + plant.SunlightEnergy);

        if (_solutions.TryGetSolution(ent.Owner, plant.Solution, out var solution))
        {
            var drunk = _solutions.SplitSolution(solution.Value, plant.WaterPerUpdate);
            var water = (float) drunk.GetTotalPrototypeQuantity(CFPlantComponent.Water);
            plant.Resource = Math.Min(plant.ResourceMax, plant.Resource + water);
        }

        if (plant.GrowthLevel < 1f && plant.Energy >= plant.EnergyCost && plant.Resource >= plant.ResourceCost)
        {
            plant.Energy -= plant.EnergyCost;
            plant.Resource -= plant.ResourceCost;
            plant.GrowthLevel = Math.Min(1f, plant.GrowthLevel + plant.GrowthPerUpdate);
        }

        Dirty(ent);
    }

    [SubscribeLocalEvent]
    private void OnExamine(Entity<CFPlantComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Energy < ent.Comp.EnergyCost)
            args.PushMarkup(Loc.GetString("cf-farming-low-energy"));

        if (ent.Comp.Resource < ent.Comp.ResourceCost)
            args.PushMarkup(Loc.GetString("cf-farming-low-water"));
    }

    [SubscribeLocalEvent]
    private void OnSeedInteract(Entity<CFSeedComponent> seed, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !CanPlant(seed, args.ClickLocation, args.User))
            return;

        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            args.User,
            seed.Comp.PlantingTime,
            new CFPlantSeedDoAfterEvent(GetNetCoordinates(args.ClickLocation)),
            seed)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            BreakOnHandChange = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnSeedPlanted(Entity<CFSeedComponent> seed, ref CFPlantSeedDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        TryPlant(seed, GetCoordinates(args.Coordinates), args.User);
    }

    /// <summary>Plants the seed, using it up. Returns the plant, or null if the spot will not do.</summary>
    public EntityUid? TryPlant(Entity<CFSeedComponent> seed, EntityCoordinates position, EntityUid? user)
    {
        if (!CanPlant(seed, position, user))
            return null;

        var plant = Spawn(seed.Comp.PlantProto, _transform.ToMapCoordinates(position));
        QueueDel(seed);
        return plant;
    }

    public bool CanPlant(Entity<CFSeedComponent> seed, EntityCoordinates position, EntityUid? user)
    {
        if (_transform.GetGrid(position) is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return false;

        var tile = _maps.GetTileRef(grid, gridComp, position);
        if (!seed.Comp.SoilTile.Contains(_turf.GetContentTileDefinition(tile).ID))
        {
            Tell(user, Loc.GetString("cf-farming-soil-wrong", ("seed", seed.Owner)));
            return false;
        }

        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile.GridIndices);
        while (anchored.MoveNext(out var other))
        {
            if (!HasComp<CFPlantComponent>(other))
                continue;

            Tell(user, Loc.GetString("cf-farming-soil-occupied"));
            return false;
        }

        return true;
    }

    private void Tell(EntityUid? user, string message)
    {
        if (user is { } target)
            _popup.PopupEntity(message, target, target);
    }

    [SubscribeLocalEvent]
    private void OnInteractHand(Entity<CFPlantGatherableComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || !CanHarvest(ent))
            return;

        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            args.User,
            ent.Comp.GatherDelay,
            new CFPlantGatherDoAfterEvent(),
            ent,
            ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnGatherFinished(Entity<CFPlantGatherableComponent> ent, ref CFPlantGatherDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        Harvest(ent);
    }

    public bool CanHarvest(Entity<CFPlantGatherableComponent> ent)
    {
        return !TryComp(ent, out CFPlantComponent? plant) || plant.GrowthLevel >= ent.Comp.GrowthLevelToHarvest;
    }

    public bool Harvest(Entity<CFPlantGatherableComponent> ent)
    {
        if (!CanHarvest(ent))
            return false;

        var position = _transform.GetMapCoordinates(ent);
        foreach (var loot in _tables.GetSpawns(ent.Comp.Loot))
        {
            Spawn(loot, position.Offset(_random.NextVector2(ent.Comp.GatherOffset)));
        }

        _audio.PlayPvs(ent.Comp.GatherSound, Transform(ent).Coordinates);

        if (ent.Comp.DeleteAfterHarvest)
        {
            _destructible.DestroyEntity(ent);
        }
        else if (TryComp(ent, out CFPlantComponent? plant))
        {
            plant.GrowthLevel = Math.Max(0f, plant.GrowthLevel - ent.Comp.GrowthCostHarvest);
            Dirty(ent, plant);
        }

        return true;
    }
}
