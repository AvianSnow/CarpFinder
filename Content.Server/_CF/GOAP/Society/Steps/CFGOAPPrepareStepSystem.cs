using Content.Shared.CF.Cooking;
using Content.Shared.CF.GOAP;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Temperature.Systems;
using Content.Shared.Tools.Components;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>
/// Takes the food it holds one stage nearer to eating, at the place the tribe does that: wheat is ground at the
/// mortar, flour is mixed into the well or a bucket, dough and raw meat are held over the fire, and a loaf is put
/// down and cut with the knife. Each is what a player does with the same things, so the same recipes run.
/// After one stage the errand is over; thinking sees what it holds now and plans the next.
/// </summary>
public sealed partial class CFGOAPPrepareStepSystem : CFGOAPErrandStepSystem
{
    /// <summary>What a grill gives, the way the Prestidigitation cantrip heats food.</summary>
    private const float CookTemperature = 420f;

    private static readonly TimeSpan CookTime = TimeSpan.FromSeconds(4);

    /// <summary>Grinding and slicing are stock do-afters. Longer than this and something stopped it.</summary>
    private static readonly TimeSpan WorkTime = TimeSpan.FromSeconds(8);

    private const float SliceReach = 1.5f;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTemperatureSystem _temperature = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Prepare;

    protected override TimeSpan Dwell => TimeSpan.FromSeconds(0.5);

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        target = default;
        if (!Tribes.TryHeldFood(errand.Uid, out var food)
            || !Society.TryFindStation(errand.Uid, errand.Tribe.Comp, Xform.GetMapCoordinates(errand.Uid), food, out target))
            return false;

        errand.Social.Working = food;
        return true;
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (TryGetErrand(ent, out var before))
        {
            before.Social.Working = null;
            before.Social.Acted = false;
        }

        base.Begin(ent, action);

        // The fire and the well serve everyone at once. A mortar or a knife is one member's while it works.
        if (TryGetErrand(ent, out var errand) && errand.Social.Errand is { } station && !HasComp<ItemComponent>(station))
            Tribes.Release(errand.Tribe.Comp, station, ent);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        var uid = errand.Uid;
        var social = errand.Social;
        if (social.Working is not { } food)
            return true;

        var now = Timing.CurTime;
        var arrived = social.ArrivedAt ?? now;
        if (HasComp<CFMortarComponent>(target))
        {
            // Grinding is a do-after: the flour appears in the hand when it ends.
            if (!social.Acted)
            {
                if (!Hands.IsHolding(uid, food))
                    return true;

                _interaction.InteractUsing(uid, food, target, Transform(target).Coordinates, checkCanInteract: false, checkCanUse: false);
                social.Acted = true;
                return false;
            }

            return TerminatingOrDeleted(food) || now >= arrived + WorkTime;
        }

        if (HasComp<ToolComponent>(target))
            return Slice(errand, target, food, arrived);

        if (Tribes.TryGetFire(errand.Tribe.Comp, out var fire) && fire == target)
        {
            if (now < arrived + CookTime)
                return false;

            if (Hands.IsHolding(uid, food))
                _temperature.SetCookedTemperature(food, CookTemperature);

            return true;
        }

        // Water: flour mixed into it is dough at once.
        if (Hands.IsHolding(uid, food))
            _interaction.InteractUsing(uid, food, target, Transform(target).Coordinates, checkCanInteract: false, checkCanUse: false);

        return true;
    }

    /// <summary>
    /// Puts the loaf down, takes the knife, cuts, puts the knife back and picks up one slice. The rest lie there
    /// for whoever is hungry or stocking the stores.
    /// </summary>
    private bool Slice(CFErrand errand, EntityUid knife, EntityUid loaf, TimeSpan arrived)
    {
        var uid = errand.Uid;
        var social = errand.Social;
        if (!social.Acted)
        {
            if (Hands.IsHolding(uid, loaf))
                Hands.TryDrop(uid, loaf);

            if (!Hands.IsHolding(uid, knife) && (Containers.IsEntityInContainer(knife) || !Hands.TryPickupAnyHand(uid, knife)))
                return true;

            _interaction.InteractUsing(uid, knife, loaf, Transform(loaf).Coordinates, checkCanInteract: false, checkCanUse: false);
            social.Acted = true;
            return false;
        }

        if (!TerminatingOrDeleted(loaf) && Timing.CurTime < arrived + WorkTime)
            return false;

        if (Hands.IsHolding(uid, knife))
            Hands.TryDrop(uid, knife);

        var here = Xform.GetMapCoordinates(uid);
        var nearest = float.MaxValue;
        EntityUid? slice = null;
        foreach (var item in _lookup.GetEntitiesInRange<ItemComponent>(here, SliceReach))
        {
            if (Containers.IsEntityInContainer(item) || !Tribes.IsEdible(item))
                continue;

            var distance = (Xform.GetMapCoordinates(item).Position - here.Position).Length();
            if (distance >= nearest)
                continue;

            nearest = distance;
            slice = item;
        }

        if (slice is { } piece)
            Hands.TryPickupAnyHand(uid, piece);

        return true;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        // A knife still in hand when the plan changes goes back on the ground, so its hands are free.
        if (TryGetErrand(ent, out var before) && before.Social.Errand is { } knife && HasComp<ToolComponent>(knife) && Hands.IsHolding(ent.Owner, knife))
            Hands.TryDrop(ent.Owner, knife);

        base.End(ent, action);
        if (TryGetErrand(ent, out var errand))
        {
            errand.Social.Working = null;
            errand.Social.Acted = false;
        }
    }
}
