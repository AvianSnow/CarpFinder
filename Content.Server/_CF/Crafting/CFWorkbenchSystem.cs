using System.Numerics;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Events;
using Content.Shared.Placeable;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CF.Crafting;

/// <summary>
/// Learning formulas and the Craft activity at a workbench. A craft takes the recipe's few seconds,
/// then one Crafting check decides what happens to the materials on the bench.
/// Based on CP14's <c>CP14WorkbenchSystem</c>.
/// </summary>
public sealed partial class CFWorkbenchSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFCraftingSystem _crafting = default!;

    [SubscribeLocalEvent]
    private void OnFormulaUsed(Entity<CFFormulaComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var learned = Learn(ent, args.User);
        _popup.PopupEntity(
            Loc.GetString(learned > 0 ? "cf-craft-learned" : "cf-craft-learned-nothing", ("count", learned)),
            args.User,
            args.User);
    }

    /// <summary>Adds what the formula teaches to the user's book and returns how many recipes were new.</summary>
    public int Learn(Entity<CFFormulaComponent> formula, EntityUid user)
    {
        var book = EnsureComp<CFFormulaBookComponent>(user);
        var learned = 0;

        foreach (var recipe in formula.Comp.Recipes)
        {
            if (book.Known.Add(recipe))
                learned++;
        }

        if (formula.Comp.AllBasic)
        {
            foreach (var recipe in _prototypes.EnumeratePrototypes<CFCraftingRecipePrototype>())
            {
                var (level, rarity) = ItemStats(recipe);
                if (level == 0 && rarity == CFRarity.Common && book.Known.Add(recipe.ID))
                    learned++;
            }
        }

        Dirty(user, book);
        return learned;
    }

    private (int Level, CFRarity Rarity) ItemStats(CFCraftingRecipePrototype recipe)
    {
        return _prototypes.Index(recipe.Result).TryComp(out CFItemComponent? item, Factory)
            ? (item.Level, item.Rarity)
            : (0, CFRarity.Common);
    }

    public int Dc(CFCraftingRecipePrototype recipe)
    {
        var (level, rarity) = ItemStats(recipe);
        return CFCraftingRules.Dc(level, rarity);
    }

    /// <summary>The locale id of why <paramref name="user"/> cannot attempt the recipe, or null if they can.</summary>
    public string? BlockedReason(EntityUid user, CFCraftingRecipePrototype recipe)
    {
        var rank = _crafting.Rank(user);
        if (rank < CFProficiencyRank.Trained)
            return "cf-craft-blocked-untrained";

        var (level, _) = ItemStats(recipe);
        var userLevel = CompOrNull<CFCombatantComponent>(user)?.Level ?? 0;
        if (level > userLevel)
            return "cf-craft-blocked-level";

        return rank < CFCraftingRules.RequiredRank(level) ? "cf-craft-blocked-rank" : null;
    }

    private bool Knows(EntityUid user, CFCraftingRecipePrototype recipe)
    {
        return TryComp(user, out CFFormulaBookComponent? book) && book.Known.Contains(recipe.ID);
    }

    private static bool Offers(CFWorkbenchComponent bench, CFCraftingRecipePrototype recipe)
    {
        return bench.RecipeTags.Contains(recipe.Tag);
    }

    /// <summary>A copy, because using materials up changes the placer's own set.</summary>
    private HashSet<EntityUid> Placed(EntityUid bench)
    {
        return TryComp(bench, out ItemPlacerComponent? placer)
            ? new HashSet<EntityUid>(placer.PlacedEntities)
            : new HashSet<EntityUid>();
    }

    private bool HasMaterials(CFCraftingRecipePrototype recipe, HashSet<EntityUid> placed)
    {
        foreach (var requirement in recipe.Requirements)
        {
            if (!requirement.IsMet(EntityManager, placed))
                return false;
        }

        return true;
    }

    private bool CanStart(Entity<CFWorkbenchComponent> bench, EntityUid user, CFCraftingRecipePrototype recipe)
    {
        return Offers(bench.Comp, recipe)
               && Knows(user, recipe)
               && BlockedReason(user, recipe) == null
               && HasMaterials(recipe, Placed(bench));
    }

    [SubscribeLocalEvent]
    private void OnBeforeUiOpen(Entity<CFWorkbenchComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        SendState(ent, args.User);
    }

    [SubscribeLocalEvent]
    private void OnItemPlaced(Entity<CFWorkbenchComponent> ent, ref ItemPlacedEvent args)
    {
        RefreshOpenUis(ent);
    }

    [SubscribeLocalEvent]
    private void OnItemRemoved(Entity<CFWorkbenchComponent> ent, ref ItemRemovedEvent args)
    {
        RefreshOpenUis(ent);
    }

    private void RefreshOpenUis(Entity<CFWorkbenchComponent> ent)
    {
        foreach (var actor in _ui.GetActors(ent.Owner, CFWorkbenchUiKey.Key))
        {
            SendState(ent, actor);
            return; // The UI is single-user, and the state belongs to that user.
        }
    }

    private void SendState(Entity<CFWorkbenchComponent> ent, EntityUid user)
    {
        _ui.SetUiState(ent.Owner, CFWorkbenchUiKey.Key, new CFWorkbenchState(Entries(ent, user)));
    }

    /// <summary>The window's list: only recipes this bench makes and this user has learned.</summary>
    public List<CFWorkbenchEntry> Entries(Entity<CFWorkbenchComponent> ent, EntityUid user)
    {
        var entries = new List<CFWorkbenchEntry>();
        var placed = Placed(ent);

        foreach (var recipe in _prototypes.EnumeratePrototypes<CFCraftingRecipePrototype>())
        {
            if (Offers(ent.Comp, recipe) && Knows(user, recipe))
                entries.Add(new CFWorkbenchEntry(recipe.ID, Dc(recipe), HasMaterials(recipe, placed), BlockedReason(user, recipe)));
        }

        return entries;
    }

    [SubscribeLocalEvent]
    private void OnCraftMessage(Entity<CFWorkbenchComponent> ent, ref CFWorkbenchCraftMessage args)
    {
        if (!_prototypes.TryIndex(args.Recipe, out var recipe) || !CanStart(ent, args.Actor, recipe))
            return;

        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            args.Actor,
            recipe.CraftTime,
            new CFCraftDoAfterEvent { Recipe = recipe.ID },
            ent,
            ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });

        if (started)
            _audio.PlayPvs(ent.Comp.CraftSound, ent);
    }

    [SubscribeLocalEvent]
    private void OnCraftFinished(Entity<CFWorkbenchComponent> ent, ref CFCraftDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !_prototypes.TryIndex(args.Recipe, out var recipe))
            return;

        args.Handled = true;
        TryCraft(ent, args.User, recipe);
        SendState(ent, args.User);
    }

    /// <summary>
    /// The Crafting check. Any success makes the item and uses the materials. A failure leaves them all on the bench.
    /// A critical failure ruins a tenth of them. Returns the degree, or null if the craft could not be attempted.
    /// </summary>
    public CFDegree? TryCraft(Entity<CFWorkbenchComponent> bench, EntityUid user, CFCraftingRecipePrototype recipe)
    {
        if (!CanStart(bench, user, recipe))
        {
            _popup.PopupEntity(Loc.GetString("cf-craft-cannot"), bench, user);
            return null;
        }

        var dc = Dc(recipe);
        var degree = _conditions.RollSkillAgainst(user, CFCraftingRules.Skill, CFAbility.Intelligence, dc, out var total);
        var success = degree.IsSuccess();
        _popup.PopupEntity(
            Loc.GetString(
                success ? "cf-craft-roll-success" : "cf-craft-roll-failure",
                ("name", Identity.Entity(user, EntityManager)),
                ("total", total),
                ("dc", dc)),
            user,
            success ? PopupType.Medium : PopupType.MediumCaution);

        var placed = Placed(bench);
        if (success)
        {
            foreach (var requirement in recipe.Requirements)
            {
                requirement.Consume(EntityManager, placed, requirement.Count);
            }

            SpawnResults(bench, recipe);
        }
        else if (degree == CFDegree.CriticalFailure)
        {
            Ruin(recipe, placed);
            _popup.PopupEntity(Loc.GetString("cf-craft-ruined"), bench, user, PopupType.MediumCaution);
        }

        return degree;
    }

    private void Ruin(CFCraftingRecipePrototype recipe, HashSet<EntityUid> placed)
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
            requirement.Consume(EntityManager, placed, units);
            ruined -= units;
        }
    }

    private void SpawnResults(EntityUid bench, CFCraftingRecipePrototype recipe)
    {
        var coordinates = Transform(bench).Coordinates;
        for (var i = 0; i < recipe.ResultCount; i++)
        {
            var result = Spawn(recipe.Result, _transform.ToMapCoordinates(coordinates));
            _transform.SetCoordinates(
                result,
                coordinates.Offset(new Vector2(_random.NextFloat(-0.25f, 0.25f), _random.NextFloat(-0.25f, 0.25f))));
        }
    }
}
