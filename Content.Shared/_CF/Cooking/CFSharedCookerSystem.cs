// Ported from Crystall Punk 14 (CP14SharedCookingSystem), MIT.
using System.Numerics;
using Content.Shared.Audio;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Fluids;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Cooking;

/// <summary>
/// Cooking in a pot, pan or pie plate (<see cref="CFFoodCookerComponent"/>), and serving the dish onto a plate
/// or into a bowl (<see cref="CFFoodHolderComponent"/>). An abstract base, so each side subscribes once.
/// </summary>
public abstract partial class CFSharedCookerSystem : EntitySystem
{
    [Dependency] protected SharedContainerSystem Containers = default!;
    [Dependency] protected SharedSolutionContainerSystem Solutions = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IngestionSystem _ingestion = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPuddleSystem _puddle = default!;

    /// <summary>Half of a burnt dish becomes this.</summary>
    private static readonly ProtoId<ReagentPrototype> BurntFoodReagent = "CFBurntFood";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CFFoodHolderComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<CFFoodHolderComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<CFFoodHolderComponent, InteractUsingEvent>(OnInteractUsing);

        SubscribeLocalEvent<CFFoodCookerComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<CFFoodCookerComponent, CFHeatedEvent>(OnHeated);
        SubscribeLocalEvent<CFFoodCookerComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<CFFoodCookerComponent, CFCookingDoAfter>(OnCookFinished);
        SubscribeLocalEvent<CFFoodCookerComponent, CFBurningDoAfter>(OnCookBurned);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var cookers = EntityQueryEnumerator<CFFoodCookerComponent>();
        while (cookers.MoveNext(out var uid, out var cooker))
        {
            // Off the heat: whatever was cooking or burning stops.
            if (now > cooker.LastHeatingTime + cooker.HeatingFrequencyRequired && _doAfter.IsRunning(cooker.DoAfterId))
                StopCooking((uid, cooker));
        }
    }

    private void OnExamined(Entity<CFFoodHolderComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.FoodData?.Name is not { } name
            || !Solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out _, out var solution)
            || solution.Volume == 0)
        {
            return;
        }

        args.PushMarkup(Loc.GetString("cf-cooking-examine", ("name", Loc.GetString(name)), ("count", solution.Volume)));
    }

    // Serving: a plate used on the pot, or the pot used on a plate.

    private void OnInteractUsing(Entity<CFFoodHolderComponent> target, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TrySlice(target, args.Used, args.User))
        {
            args.Handled = true;
            return;
        }

        if (!TryComp(args.Used, out CFFoodHolderComponent? used))
            return;

        args.Handled = TryTransferFood(target, (args.Used, used)) || TryTransferFood((args.Used, used), target);
    }

    private void OnAfterInteract(Entity<CFFoodHolderComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } other || !TryComp(other, out CFFoodHolderComponent? target))
            return;

        args.Handled = TryTransferFood(ent, (other, target));
    }

    private void OnInsertAttempt(Entity<CFFoodCookerComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled || !TryComp(ent, out CFFoodHolderComponent? holder) || holder.FoodData is null)
            return;

        if (_net.IsServer)
            _popup.PopupEntity(Loc.GetString("cf-cooking-popup-not-empty", ("name", MetaData(ent).EntityName)), ent);

        args.Cancel();
    }

    /// <summary>Starts cutting a dish that is cut where it was baked, such as a pie. The server does it.</summary>
    protected virtual bool TrySlice(Entity<CFFoodHolderComponent> dish, EntityUid tool, EntityUid user)
    {
        return false;
    }

    /// <summary>Moves a dish, with what it is, from a cooker into a plate or bowl of the same food type.</summary>
    public virtual bool TryTransferFood(Entity<CFFoodHolderComponent> target, Entity<CFFoodHolderComponent> source)
    {
        if (!source.Comp.CanGiveFood
            || !target.Comp.CanAcceptFood
            || target.Comp.FoodType != source.Comp.FoodType
            || source.Comp.FoodData is null
            || !TryComp(target, out EdibleComponent? edible)
            || !Solutions.TryGetSolution(source.Owner, source.Comp.SolutionId, out var cookerSoln, out var cookerSolution)
            || !Solutions.TryGetSolution(target.Owner, edible.Solution, out var holderSoln, out var solution))
        {
            return false;
        }

        if (solution.Volume > 0)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-cooking-popup-not-empty", ("name", MetaData(target).EntityName)), target);

            return false;
        }

        Solutions.TryTransferSolution(holderSoln.Value, cookerSolution, solution.MaxVolume);

        // One piece of trash per plate, and the last plate out of the cooker gets all that is left.
        var data = source.Comp.FoodData;
        if (data.Trash.Count > 0)
        {
            if (cookerSolution.Volume <= 0)
            {
                _ingestion.AddTrash((target.Owner, edible), data.Trash);
            }
            else if (_net.IsServer)
            {
                var trash = _random.Pick(data.Trash);
                data.Trash.Remove(trash);
                _ingestion.AddTrash((target.Owner, edible), new List<EntProtoId> { trash });
            }
        }

        ApplyFoodData(target, data);

        Dirty(target);
        Dirty(source);
        Solutions.UpdateChemicals(cookerSoln.Value);
        return true;
    }

    /// <summary>Makes the holder be this dish: name, description, flavors and layers.</summary>
    protected virtual void ApplyFoodData(Entity<CFFoodHolderComponent> ent, CFFoodData data, bool rename = true)
    {
        if (rename)
        {
            if (data.Name is { } name)
                _metaData.SetEntityName(ent, Loc.GetString(name));

            if (data.Desc is { } desc)
                _metaData.SetEntityDescription(ent, Loc.GetString(desc));
        }

        var flavors = EnsureComp<FlavorProfileComponent>(ent);
        foreach (var flavor in data.Flavors)
        {
            flavors.Flavors.Add(flavor);
        }

        ent.Comp.FoodData = new CFFoodData(data);
        if (_net.IsServer)
        {
            foreach (var layer in ent.Comp.FoodData.Visuals)
            {
                if (_random.Prob(0.5f))
                    layer.Scale = new Vector2(-1, 1);
            }
        }

        DirtyField(ent, ent.Comp, nameof(CFFoodHolderComponent.FoodData));
    }

    // Cooking.

    /// <summary>The recipe what is in the cooker would make now, or null if nothing fits.</summary>
    public CFCookingRecipePrototype? GetRecipe(Entity<CFFoodCookerComponent> ent)
    {
        if (!Containers.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            return null;

        Solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out _, out var solution);

        var tags = new List<ProtoId<TagPrototype>>();
        foreach (var contained in container.ContainedEntities)
        {
            if (TryComp(contained, out TagComponent? tag))
                tags.AddRange(tag.Tags);
        }

        // The hardest recipe that fits wins. Easy ones are the most general, so they come last.
        CFCookingRecipePrototype? best = null;
        var bestComplexity = float.MinValue;
        foreach (var recipe in _prototypes.EnumeratePrototypes<CFCookingRecipePrototype>())
        {
            if (recipe.FoodType != ent.Comp.FoodType || recipe.Requirements.Count == 0)
                continue;

            var complexity = recipe.Complexity();
            if (complexity < bestComplexity || complexity == bestComplexity && string.CompareOrdinal(recipe.ID, best!.ID) > 0)
                continue;

            var fits = true;
            foreach (var requirement in recipe.Requirements)
            {
                if (requirement.IsMet(tags, solution))
                    continue;

                fits = false;
                break;
            }

            if (!fits)
                continue;

            best = recipe;
            bestComplexity = complexity;
        }

        return best;
    }

    private void OnHeated(Entity<CFFoodCookerComponent> ent, ref CFHeatedEvent args)
    {
        if (!Containers.TryGetContainer(ent, ent.Comp.ContainerId, out var container)
            || !TryComp(ent, out CFFoodHolderComponent? holder))
        {
            return;
        }

        if (container.ContainedEntities.Count <= 0 && holder.FoodData is null)
        {
            StopCooking(ent);
            return;
        }

        ent.Comp.LastHeatingTime = _timing.CurTime;
        DirtyField(ent.Owner, ent.Comp, nameof(CFFoodCookerComponent.LastHeatingTime));

        if (_doAfter.IsRunning(ent.Comp.DoAfterId))
            return;

        if (holder.FoodData is not null)
            StartBurning(ent);
        else if (GetRecipe(ent) is { } recipe)
            StartCooking(ent, recipe);
    }

    private void OnParentChanged(Entity<CFFoodCookerComponent> ent, ref EntParentChangedMessage args)
    {
        StopCooking(ent);
    }

    private void StartCooking(Entity<CFFoodCookerComponent> ent, CFCookingRecipePrototype recipe)
    {
        _appearance.SetData(ent, CFCookingVisuals.Cooking, true);
        Start(ent, recipe.CookingTime, new CFCookingDoAfter(recipe.ID));
    }

    private void StartBurning(Entity<CFFoodCookerComponent> ent)
    {
        _appearance.SetData(ent, CFCookingVisuals.Burning, true);
        Start(ent, ent.Comp.BurnTime, new CFBurningDoAfter());
    }

    /// <summary>The cooker is its own do-after user: nobody has to stand by it, only the heat has to stay.</summary>
    private void Start(Entity<CFFoodCookerComponent> ent, TimeSpan time, DoAfterEvent ev)
    {
        var args = new DoAfterArgs(EntityManager, ent, time, ev, ent)
        {
            NeedHand = false,
            BreakOnWeightlessMove = false,
            RequireCanInteract = false,
        };

        _doAfter.TryStartDoAfter(args, out var id);
        ent.Comp.DoAfterId = id;
        _ambientSound.SetAmbience(ent, true);
    }

    protected void StopCooking(Entity<CFFoodCookerComponent> ent)
    {
        if (_doAfter.IsRunning(ent.Comp.DoAfterId))
            _doAfter.Cancel(ent.Comp.DoAfterId);

        _appearance.SetData(ent, CFCookingVisuals.Cooking, false);
        _appearance.SetData(ent, CFCookingVisuals.Burning, false);
        _ambientSound.SetAmbience(ent, false);
    }

    protected virtual void OnCookFinished(Entity<CFFoodCookerComponent> ent, ref CFCookingDoAfter args)
    {
        StopCooking(ent);

        if (args.Cancelled
            || args.Handled
            || !TryComp(ent, out CFFoodHolderComponent? holder)
            || !_prototypes.TryIndex(args.Recipe, out var recipe))
        {
            return;
        }

        args.Handled = true;
        if (CreateFoodData(ent, recipe) && holder.FoodData is { } data)
            ApplyFoodData((ent, holder), data, ent.Comp.RenameCooker);
    }

    protected virtual void OnCookBurned(Entity<CFFoodCookerComponent> ent, ref CFBurningDoAfter args)
    {
        StopCooking(ent);

        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        BurnFood(ent);
    }

    /// <summary>Stirs every ingredient into the cooker's solution and makes that the recipe's dish.</summary>
    private bool CreateFoodData(Entity<CFFoodCookerComponent> ent, CFCookingRecipePrototype recipe)
    {
        if (!Solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out var soln, out var solution)
            || !Containers.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
        {
            return false;
        }

        var data = new CFFoodData(recipe.FoodData) { CurrentRecipe = recipe.ID };

        foreach (var contained in container.ContainedEntities)
        {
            if (TryComp(contained, out EdibleComponent? food))
            {
                data.Trash.AddRange(food.Trash);

                if (Solutions.TryGetSolution(contained, food.Solution, out _, out var foodSolution))
                {
                    Solutions.TryMixAndOverflow(soln.Value, foodSolution, solution.MaxVolume, out var overflowed);
                    if (overflowed is not null)
                        _puddle.TrySplashSpillAt(ent, Transform(ent).Coordinates, overflowed, out _);
                }
            }

            if (TryComp(contained, out FlavorProfileComponent? flavors))
            {
                foreach (var flavor in flavors.Flavors)
                {
                    data.Flavors.Add(flavor);
                }
            }

            QueueDel(contained);
        }

        if (solution.Volume <= 0 || !TryComp(ent.Owner, out CFFoodHolderComponent? holder))
            return false;

        holder.FoodData = data;
        Dirty(ent.Owner, holder);
        return true;
    }

    private void BurnFood(Entity<CFFoodCookerComponent> ent)
    {
        if (!TryComp(ent, out CFFoodHolderComponent? holder)
            || holder.FoodData is null
            || !Solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out var soln, out var solution))
        {
            return;
        }

        foreach (var layer in holder.FoodData.Visuals)
        {
            layer.Color = Color.FromHex("#212121");
        }

        holder.FoodData.Name = "cf-meal-recipe-burned-trash-name";
        holder.FoodData.Desc = "cf-meal-recipe-burned-trash-desc";

        var replaced = solution.Volume / 2;
        Solutions.SplitSolution(soln.Value, replaced);
        Solutions.TryAddReagent(soln.Value, BurntFoodReagent, replaced / 2, out _);

        DirtyField(ent.Owner, holder, nameof(CFFoodHolderComponent.FoodData));
    }
}
