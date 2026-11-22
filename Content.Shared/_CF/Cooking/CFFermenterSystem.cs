using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using FixedPoint2 = Content.Shared.FixedPoint.FixedPoint2;

namespace Content.Shared.CF.Cooking;

/// <summary>Brewing in a barrel: mashing an ingredient into its water, and the mash fermenting over time.</summary>
public sealed partial class CFFermenterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private TagSystem _tags = default!;

    /// <summary>The mash this would make in the barrel, or null if it brews nothing.</summary>
    public ProtoId<ReagentPrototype>? MashOf(Entity<CFFermenterComponent> ent, EntityUid ingredient)
    {
        foreach (var (tag, mash) in ent.Comp.Mashes)
        {
            if (_tags.HasTag(ingredient, tag))
                return mash;
        }

        return null;
    }

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<CFFermenterComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || MashOf(ent, args.Used) is null)
            return;

        args.Handled = true;
        if (_net.IsClient)
            return;

        if (!TryMash(ent, args.Used))
            _popup.PopupEntity(Loc.GetString("cf-brewing-needs-water", ("amount", ent.Comp.WaterPerItem)), ent, args.User);
        else
            _popup.PopupEntity(Loc.GetString("cf-brewing-mashed", ("item", args.Used)), ent, args.User);
    }

    /// <summary>Mashes the ingredient into the barrel's water, using it up. False with too little water.</summary>
    public bool TryMash(Entity<CFFermenterComponent> ent, EntityUid ingredient)
    {
        if (MashOf(ent, ingredient) is not { } mash
            || !_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var soln, out var solution)
            || solution.GetTotalPrototypeQuantity(ent.Comp.Water) < ent.Comp.WaterPerItem)
        {
            return false;
        }

        _solutions.RemoveReagent(soln.Value, ent.Comp.Water.Id, ent.Comp.WaterPerItem);
        _solutions.TryAddReagent(soln.Value, mash.Id, ent.Comp.WaterPerItem, out _);
        _audio.PlayPvs(ent.Comp.Sound, ent);
        QueueDel(ingredient);
        return true;
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        var fermenters = EntityQueryEnumerator<CFFermenterComponent>();
        while (fermenters.MoveNext(out var uid, out var fermenter))
        {
            if (now < fermenter.NextUpdate)
                continue;

            fermenter.NextUpdate = now + fermenter.Interval;
            Ferment((uid, fermenter));
        }
    }

    /// <summary>One step: a little of each mash in the barrel becomes its drink.</summary>
    public void Ferment(Entity<CFFermenterComponent> ent)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var soln, out var solution) || solution.Volume == 0)
            return;

        foreach (var (mash, drink) in ent.Comp.Ferments)
        {
            var have = solution.GetTotalPrototypeQuantity(mash);
            if (have <= 0)
                continue;

            var amount = FixedPoint2.Min(have, ent.Comp.UnitsPerUpdate);
            _solutions.RemoveReagent(soln.Value, mash.Id, amount);
            _solutions.TryAddReagent(soln.Value, drink.Id, amount, out _);
        }
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<CFFermenterComponent> ent, ref ExaminedEvent args)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution))
            return;

        foreach (var mash in ent.Comp.Ferments.Keys)
        {
            if (solution.GetTotalPrototypeQuantity(mash) <= 0)
                continue;

            args.PushMarkup(Loc.GetString("cf-brewing-fermenting"));
            return;
        }
    }
}
