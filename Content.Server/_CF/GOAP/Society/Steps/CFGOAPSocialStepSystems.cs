using Content.Server.CF.GOAP.Steps;
using Content.Shared.CF.GOAP;
using Robust.Shared.Random;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>Walks up to a hungry tribemate and hands over the food it holds.</summary>
public sealed partial class CFGOAPGiveFoodStepSystem : CFGOAPErrandStepSystem
{
    protected override CFGOAPStep Kind => CFGOAPStep.GiveFood;

    protected override TimeSpan Dwell => TimeSpan.FromSeconds(0.5);

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        return Society.TryFindHungryMate(errand.Uid, errand.Social, errand.Tribe, Xform.GetMapCoordinates(errand.Uid), out target);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        errand.Social.Preferred = null;
        if (!Tribes.TryHeldFood(errand.Uid, out var food) || !Hands.TryDrop(errand.Uid, food))
            return true;

        // With its hands full the food stays at its feet, and it picks it up from there.
        Hands.TryPickupAnyHand(target, food);
        var name = CompOrNull<CFGOAPSocialComponent>(target)?.Name ?? Name(target);
        Tribes.Say(errand.Uid, Tribes.TakeLine(errand.Social) ?? Loc.GetString("cf-tribe-line-share", ("name", name)));
        return true;
    }
}

/// <summary>
/// Walks up to a tribemate and talks. It passes on the newest thing it remembers that the other does not know, which
/// is how news gets round the tribe. With nothing new, it is small talk.
/// </summary>
public sealed partial class CFGOAPChatStepSystem : CFGOAPErrandStepSystem
{
    private const int SmallTalkLines = 5;

    /// <summary>The one it talked to does not turn round and start a chat of its own for this long.</summary>
    private static readonly TimeSpan ListenerRest = TimeSpan.FromSeconds(15);

    [Dependency] private IRobustRandom _random = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Chat;

    protected override float Reach => 1.8f;

    protected override TimeSpan Dwell => TimeSpan.FromSeconds(0.5);

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        return Society.TryFindChatMate(errand.Uid, errand.Social, errand.Tribe, Xform.GetMapCoordinates(errand.Uid), out target);
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        var social = errand.Social;
        var tribe = errand.Tribe.Comp;
        var now = Timing.CurTime;
        social.Preferred = null;
        social.NextChat = now + TimeSpan.FromSeconds(_random.NextFloat(tribe.ChatMin, tribe.ChatMax));
        if (TryComp(target, out CFGOAPSocialComponent? listener) && listener.NextChat < now + ListenerRest)
            listener.NextChat = now + ListenerRest;

        if (Tribes.TakeLine(social) is { } line)
        {
            Tribes.Say(errand.Uid, line);
            return true;
        }

        if (News(social, target, listener) is { } news)
        {
            Tribes.Say(errand.Uid, news, news);
            return true;
        }

        Tribes.Say(errand.Uid, Loc.GetString($"cf-tribe-line-chat-{_random.Next(1, SmallTalkLines + 1)}"));
        return true;
    }

    /// <summary>The newest thing it remembers that this listener did not tell it and does not already know.</summary>
    private static string? News(CFGOAPSocialComponent social, EntityUid listenerUid, CFGOAPSocialComponent? listener)
    {
        for (var i = social.Memory.Count - 1; i >= 0; i--)
        {
            var memory = social.Memory[i];
            if (memory.From == listenerUid)
                continue;

            var known = false;
            if (listener != null)
            {
                foreach (var theirs in listener.Memory)
                {
                    known |= theirs.Text == memory.Text;
                }
            }

            if (!known)
                return memory.Text;
        }

        return null;
    }
}

/// <summary>
/// Stands off from a stranger the tribe is talking to, and does not attack. The leader makes the tribe's demand. It
/// runs until thinking changes the plan: the stranger pays, the tribe's patience runs out, or blood is drawn.
/// </summary>
public sealed partial class CFGOAPParleyStepSystem : CFGOAPStepSystem
{
    /// <summary>How far off it stands, in tiles: near enough to be heard, too far to be hit.</summary>
    private const float Standoff = 3.5f;

    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFGOAPTribeSystem _tribes = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Parley;

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is not { } target || TerminatingOrDeleted(target) || !_tribes.TryGetTribe(ent, out var tribe))
            return true;

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(target);
        var offset = there.Position - here.Position;
        if (offset.Length() > Standoff)
        {
            Steering.Navigate(ent, there.Position, null, alone: true);
            return false;
        }

        Steering.Stop(ent.Owner);
        if (offset.LengthSquared() > 0.01f)
            _transform.SetWorldRotation(ent, Angle.FromWorldVec(offset));

        _tribes.Demand(tribe, ent, target);
        return false;
    }
}
