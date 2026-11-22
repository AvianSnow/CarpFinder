using System.Numerics;
using Content.Server.CF.GOAP.Steps;
using Content.Shared.CF.GOAP;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>A tribe member on an errand, with its tribe.</summary>
public readonly record struct CFErrand(Entity<CFGOAPComponent> Creature, CFGOAPSocialComponent Social, Entity<CFGOAPTribeComponent> Tribe)
{
    public EntityUid Uid => Creature.Owner;
}

/// <summary>
/// A step that walks up to something of the tribe's and does one thing there. Most of a tribe's work is this:
/// pick what to go to, fetch a tool for it if one is needed, walk there, wait a moment, do it.
/// The thing is claimed while it lasts, so two members do not go for the same plot or the same piece of bread.
/// </summary>
public abstract partial class CFGOAPErrandStepSystem : CFGOAPStepSystem
{
    /// <summary>An errand that has taken this long is given up, so nothing can hold a member for good.</summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(60);

    private const float PickUpReach = 1.2f;

    private static readonly Vector2[] Sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] protected SharedContainerSystem Containers = default!;
    [Dependency] protected SharedHandsSystem Hands = default!;
    [Dependency] protected SharedTransformSystem Xform = default!;
    [Dependency] protected CFGOAPSocietySensorSystem Society = default!;
    [Dependency] protected CFGOAPTribeSystem Tribes = default!;

    /// <summary>How near it has to stand, in tiles.</summary>
    protected virtual float Reach => 1.6f;

    /// <summary>How long it stands there before the thing is done.</summary>
    protected virtual TimeSpan Dwell => TimeSpan.FromSeconds(1);

    /// <summary>What to go to. False ends the step at once.</summary>
    protected abstract bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target);

    /// <summary>Something to have in hand first, such as a bucket. Null when the errand needs nothing.</summary>
    protected virtual EntityUid? PickTool(CFErrand errand, EntityUid target)
    {
        return null;
    }

    /// <summary>Somewhere to go on the way, such as the well with an empty bucket. Null when there is nowhere.</summary>
    protected virtual EntityUid? Detour(CFErrand errand, EntityUid target)
    {
        return null;
    }

    /// <summary>What it does at the detour.</summary>
    protected virtual void AtDetour(CFErrand errand, EntityUid detour)
    {
    }

    /// <summary>Does the thing. Returns true when the errand is over, or false to be called again next tick.</summary>
    protected abstract bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action);

    protected bool TryGetErrand(Entity<CFGOAPComponent> ent, out CFErrand errand)
    {
        errand = default;
        if (!TryComp(ent, out CFGOAPSocialComponent? social) || !Tribes.TryGetTribe(ent, out var tribe))
            return false;

        errand = new CFErrand(ent, social, tribe);
        return true;
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryGetErrand(ent, out var errand))
            return;

        var social = errand.Social;
        social.Errand = null;
        social.Tool = null;
        social.ArrivedAt = null;
        social.WalkTarget = null;
        if (!TryPick(errand, action, out var target))
            return;

        social.Errand = target;
        Tribes.Claim(errand.Tribe.Comp, target, ent);
        if (PickTool(errand, target) is { } tool)
        {
            social.Tool = tool;
            Tribes.Claim(errand.Tribe.Comp, tool, ent);
        }
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryGetErrand(ent, out var errand)
            || errand.Social.Errand is not { } target
            || TerminatingOrDeleted(target)
            || Timing.CurTime >= ent.Comp.StepTime + GiveUpAfter)
            return true;

        var social = errand.Social;
        if (social.Tool is { } tool)
        {
            if (TerminatingOrDeleted(tool))
                return true;

            if (!Hands.IsHolding(ent.Owner, tool))
            {
                // Somebody else has it now.
                if (Containers.IsEntityInContainer(tool))
                    return true;

                if (!WalkTo(errand, tool, PickUpReach))
                    return false;

                if (!Hands.TryPickupAnyHand(ent, tool))
                    return true;
            }
        }

        if (Detour(errand, target) is { } detour)
        {
            if (WalkTo(errand, detour, Reach))
                AtDetour(errand, detour);

            return false;
        }

        if (!WalkTo(errand, target, Reach))
            return false;

        Steering.Stop(ent.Owner);
        Face(ent, target);
        social.ArrivedAt ??= Timing.CurTime;
        return Timing.CurTime >= social.ArrivedAt + Dwell && Finish(errand, target, action);
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryGetErrand(ent, out var errand))
            return;

        var social = errand.Social;
        if (social.Errand is { } target)
            Tribes.Release(errand.Tribe.Comp, target, ent);

        if (social.Tool is { } tool)
            Tribes.Release(errand.Tribe.Comp, tool, ent);

        social.Errand = null;
        social.Tool = null;
        social.ArrivedAt = null;
        social.WalkTarget = null;
    }

    /// <summary>Keeps walking up to something. Returns true once it is within <paramref name="reach"/> of it.</summary>
    protected bool WalkTo(CFErrand errand, EntityUid target, float reach)
    {
        var uid = errand.Uid;
        var here = Xform.GetMapCoordinates(uid);
        var there = Xform.GetMapCoordinates(target);
        if (here.MapId == there.MapId && (there.Position - here.Position).Length() <= reach)
            return true;

        // A tribemate moves, so it is walked up to where it is now. Anything else has a spot beside it picked once.
        if (HasComp<MobStateComponent>(target))
        {
            Steering.Navigate(uid, there.Position, null, alone: true);
            return false;
        }

        var social = errand.Social;
        if (social.WalkTarget != target)
        {
            social.WalkTarget = target;
            social.WalkPoint = StandPoint(uid, here, there);
        }

        Steering.Travel(uid, social.WalkPoint, alone: true);
        return false;
    }

    /// <summary>
    /// Where to stand to use something. A well or a crate fills its tile, so it cannot be walked onto: the nearest
    /// open tile beside it is used instead.
    /// </summary>
    private Vector2 StandPoint(EntityUid uid, MapCoordinates here, MapCoordinates there)
    {
        if (Steering.IsWalkable(uid, there))
            return there.Position;

        var best = there.Position;
        var nearest = float.MaxValue;
        foreach (var side in Sides)
        {
            var point = there.Offset(side);
            var distance = (point.Position - here.Position).Length();
            if (distance >= nearest || !Steering.IsWalkable(uid, point))
                continue;

            nearest = distance;
            best = point.Position;
        }

        return best;
    }

    protected void Face(EntityUid uid, EntityUid target)
    {
        var offset = Xform.GetWorldPosition(target) - Xform.GetWorldPosition(uid);
        if (offset.LengthSquared() > 0.01f)
            Xform.SetWorldRotation(uid, Angle.FromWorldVec(offset));
    }
}
