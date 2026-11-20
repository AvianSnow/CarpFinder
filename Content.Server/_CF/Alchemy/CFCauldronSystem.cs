using Content.Server.CF.Crafting;
using Content.Shared.Audio;
using Content.Shared.CF.Alchemy;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Cooking;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Preferences;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CF.Alchemy;

/// <summary>
/// Brewing in a <see cref="CFCauldronComponent"/>: on a fire, with water and the ingredients of a recipe it offers,
/// it boils for the recipe's craft time, then the brewer rolls Crafting as at a workbench. A success makes the
/// potions and uses the ingredients and water up; a failure keeps them for another try; a critical failure ruins a
/// tenth of them and spoils the water.
/// </summary>
public sealed partial class CFCauldronSystem : EntitySystem
{
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFWorkbenchSystem _workbench = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    private static readonly TimeSpan ComplaintEvery = TimeSpan.FromSeconds(10);

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFCauldronComponent>();
        while (query.MoveNext(out var uid, out var cauldron))
        {
            if (_doAfter.IsRunning(cauldron.DoAfterId) && now > cauldron.LastHeatingTime + cauldron.HeatingFrequencyRequired)
                StopBrewing((uid, cauldron));
        }
    }

    /// <summary>Whoever puts something in tends the brew. A mug or bucket is poured, not put in.</summary>
    [SubscribeLocalEvent]
    private void OnStorageInteractAttempt(Entity<CFCauldronComponent> ent, ref StorageInteractUsingAttemptEvent args)
    {
        ent.Comp.Brewer = args.User;
        if (HasComp<SolutionTransferComponent>(args.Used) || HasComp<InjectorComponent>(args.Used))
            args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnPoured(Entity<CFCauldronComponent> ent, ref SolutionTransferredEvent args)
    {
        ent.Comp.Brewer = args.User;
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<CFCauldronComponent> ent, ref ExaminedEvent args)
    {
        if (_doAfter.IsRunning(ent.Comp.DoAfterId))
        {
            args.PushMarkup(Loc.GetString("cf-brew-examine-bubbling"));
            return;
        }

        if (FindRecipe(ent, Contents(ent)) is not { } recipe)
            return;

        if (Water(ent) < ent.Comp.WaterPerBrew)
            args.PushMarkup(Loc.GetString("cf-brew-examine-water", ("amount", ent.Comp.WaterPerBrew)));
        else
            args.PushMarkup(Loc.GetString("cf-brew-examine-ready", ("result", ResultName(recipe))));
    }

    /// <summary>Each second on a fire. The boil starts when a known recipe's makings are in, and stops off the heat.</summary>
    [SubscribeLocalEvent]
    private void OnHeated(Entity<CFCauldronComponent> ent, ref CFHeatedEvent args)
    {
        ent.Comp.LastHeatingTime = _timing.CurTime;
        if (_doAfter.IsRunning(ent.Comp.DoAfterId))
            return;

        var recipe = FindRecipe(ent, Contents(ent));
        if (recipe == null || Water(ent) < ent.Comp.WaterPerBrew)
            return;

        if (Refusal(ent, recipe) is { } reason)
        {
            Complain(ent, reason);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, ent, recipe.CraftTime, new CFBrewDoAfterEvent(recipe.ID), ent)
        {
            BreakOnMove = false,
            BreakOnDamage = false,
            NeedHand = false,
        };
        if (!_doAfter.TryStartDoAfter(doAfter, out var id))
            return;

        ent.Comp.DoAfterId = id;
        _ambient.SetAmbience(ent, true);
        _popup.PopupEntity(Loc.GetString("cf-brew-start", ("name", Identity.Entity(ent, EntityManager))), ent);
    }

    [SubscribeLocalEvent]
    private void OnBrewFinished(Entity<CFCauldronComponent> ent, ref CFBrewDoAfterEvent args)
    {
        ent.Comp.DoAfterId = null;
        _ambient.SetAmbience(ent, false);
        if (args.Cancelled || args.Handled || !_prototypes.TryIndex<CFCraftingRecipePrototype>(args.Recipe, out var recipe))
            return;

        args.Handled = true;
        var contents = Contents(ent);
        if (!Fits(recipe, contents) || Water(ent) < ent.Comp.WaterPerBrew)
            return;

        if (Refusal(ent, recipe) is { } reason)
        {
            Complain(ent, reason);
            return;
        }

        var brewer = ent.Comp.Brewer!.Value;
        var dc = _workbench.Dc(recipe);
        var degree = _conditions.RollSkillAgainst(brewer, CFCraftingRules.Skill, CFAbility.Intelligence, dc, out var total);
        var success = degree.IsSuccess();
        _popup.PopupEntity(
            Loc.GetString(
                success ? "cf-craft-roll-success" : "cf-craft-roll-failure",
                ("name", Identity.Entity(brewer, EntityManager)),
                ("total", total),
                ("dc", dc)),
            brewer,
            success ? PopupType.Medium : PopupType.MediumCaution);

        if (success)
        {
            foreach (var requirement in recipe.Requirements)
            {
                requirement.Consume(EntityManager, contents, requirement.Count);
            }

            RemoveWater(ent, ent.Comp.WaterPerBrew);
            var count = recipe.ResultCount + (degree == CFDegree.CriticalSuccess ? 1 : 0);
            for (var i = 0; i < count; i++)
            {
                var result = Spawn(recipe.Result, Transform(ent).Coordinates);
                if (_containers.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
                    _containers.Insert(result, container);
            }

            _popup.PopupEntity(Loc.GetString("cf-brew-done", ("result", ResultName(recipe))), ent, brewer, PopupType.Medium);
            return;
        }

        if (degree != CFDegree.CriticalFailure)
        {
            _popup.PopupEntity(Loc.GetString("cf-brew-failed"), ent, brewer, PopupType.MediumCaution);
            return;
        }

        Ruin(recipe, contents);
        var spoiled = RemoveWater(ent, ent.Comp.WaterPerBrew);
        if (_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out var soln, out _))
            _solutions.TryAddReagent(soln.Value, ent.Comp.SpoiledReagent, spoiled);

        _popup.PopupEntity(Loc.GetString("cf-brew-spoiled"), ent, brewer, PopupType.MediumCaution);
    }

    /// <summary>The recipe its contents fit best: the one that asks for the most, among those the cauldron offers.</summary>
    public CFCraftingRecipePrototype? FindRecipe(Entity<CFCauldronComponent> ent, HashSet<EntityUid> contents)
    {
        if (!TryComp(ent, out CFWorkbenchComponent? bench) || contents.Count == 0)
            return null;

        CFCraftingRecipePrototype? best = null;
        var bestAsks = 0;
        foreach (var recipe in _prototypes.EnumeratePrototypes<CFCraftingRecipePrototype>())
        {
            if (!bench.RecipeTags.Contains(recipe.Tag) || !Fits(recipe, contents))
                continue;

            var asks = 0;
            foreach (var requirement in recipe.Requirements)
            {
                asks += requirement.Count;
            }

            if (asks <= bestAsks)
                continue;

            best = recipe;
            bestAsks = asks;
        }

        return best;
    }

    private bool Fits(CFCraftingRecipePrototype recipe, HashSet<EntityUid> contents)
    {
        foreach (var requirement in recipe.Requirements)
        {
            if (!requirement.IsMet(EntityManager, contents))
                return false;
        }

        return true;
    }

    /// <summary>The locale id of why the brewer cannot make this, or null.</summary>
    private string? Refusal(Entity<CFCauldronComponent> ent, CFCraftingRecipePrototype recipe)
    {
        if (ent.Comp.Brewer is not { } brewer || !Exists(brewer) || TerminatingOrDeleted(brewer))
            return "cf-brew-no-brewer";

        if (!_workbench.Knows(brewer, recipe))
            return "cf-brew-unknown";

        return _workbench.BlockedReason(brewer, recipe);
    }

    private void Complain(Entity<CFCauldronComponent> ent, string reason)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextComplaint)
            return;

        ent.Comp.NextComplaint = now + ComplaintEvery;
        var message = Loc.GetString(reason);
        if (ent.Comp.Brewer is { } brewer && Exists(brewer))
            _popup.PopupEntity(message, ent, brewer, PopupType.MediumCaution);
        else
            _popup.PopupEntity(message, ent);
    }

    /// <summary>A copy of what is in it, because using ingredients up changes the container.</summary>
    private HashSet<EntityUid> Contents(Entity<CFCauldronComponent> ent)
    {
        return _containers.TryGetContainer(ent, ent.Comp.ContainerId, out var container)
            ? new HashSet<EntityUid>(container.ContainedEntities)
            : new HashSet<EntityUid>();
    }

    private FixedPoint2 Water(Entity<CFCauldronComponent> ent)
    {
        return _solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out _, out var solution)
            ? solution.GetTotalPrototypeQuantity(ent.Comp.Water)
            : FixedPoint2.Zero;
    }

    private FixedPoint2 RemoveWater(Entity<CFCauldronComponent> ent, int amount)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out var soln, out _))
            return FixedPoint2.Zero;

        return _solutions.RemoveReagent(soln.Value, new ReagentQuantity(new ReagentId(ent.Comp.Water, null), amount));
    }

    /// <summary>A critical failure ruins a tenth of the ingredients, as at a workbench.</summary>
    private void Ruin(CFCraftingRecipePrototype recipe, HashSet<EntityUid> contents)
    {
        var total = 0;
        foreach (var requirement in recipe.Requirements)
        {
            total += requirement.Count;
        }

        var ruined = CFCraftingRules.RuinedUnits(total);
        foreach (var requirement in recipe.Requirements)
        {
            if (ruined <= 0)
                return;

            var units = Math.Min(ruined, requirement.Count);
            requirement.Consume(EntityManager, contents, units);
            ruined -= units;
        }
    }

    private void StopBrewing(Entity<CFCauldronComponent> ent)
    {
        if (_doAfter.IsRunning(ent.Comp.DoAfterId))
            _doAfter.Cancel(ent.Comp.DoAfterId);

        ent.Comp.DoAfterId = null;
        _ambient.SetAmbience(ent, false);
    }

    private string ResultName(CFCraftingRecipePrototype recipe)
    {
        return _prototypes.Index(recipe.Result).Name;
    }
}
