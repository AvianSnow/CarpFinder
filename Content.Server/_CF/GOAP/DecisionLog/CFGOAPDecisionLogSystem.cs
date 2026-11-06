using System.Globalization;
using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.DecisionLog;

/// <summary>
/// DEBUG, to be removed once the AI has settled. Watches brains from outside once a tick, so the AI code has no logging
/// in it. Two changes in one tick show as their result, and a one-action step can finish before it is seen.
/// </summary>
public sealed partial class CFGOAPDecisionLogSystem : EntitySystem
{
    /// <summary>Flips with every swing, so it would drown out everything else.</summary>
    private const uint Ignored = (uint) CFGOAPFact.ActionsLeft;

    [Dependency] private IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        WatchAll();
    }

    /// <summary>Brings every log up to date now. A test reading one at the end calls this first.</summary>
    public void WatchAll()
    {
        var query = EntityQueryEnumerator<CFGOAPComponent>();
        while (query.MoveNext(out var uid, out var goap))
        {
            Watch(uid, goap, EnsureComp<CFGOAPDecisionLogComponent>(uid));
        }
    }

    private void Watch(EntityUid uid, CFGOAPComponent goap, CFGOAPDecisionLogComponent log)
    {
        if (goap.Goal != log.Goal)
        {
            log.Goal = goap.Goal;
            Add(log, $"goal {goap.Goal?.Id ?? "none"}");
        }

        var plan = string.Join(", ", goap.Plan);
        if (plan != log.Plan)
        {
            log.Plan = plan;
            Add(log, $"plan {plan}");
        }

        var action = goap.Step < goap.Plan.Count ? goap.Plan[goap.Step] : default(ProtoId<CFGOAPActionPrototype>?);
        if (action != log.Action)
        {
            log.Action = action;
            Add(log, $"step {action?.Id ?? "none"}");
        }

        var facts = goap.Facts & ~Ignored;
        if (facts != log.Facts)
        {
            var gained = facts & ~log.Facts;
            var lost = log.Facts & ~facts;
            log.Facts = facts;
            Add(log, $"facts +{(CFGOAPFact) gained} -{(CFGOAPFact) lost}");
        }

        if (goap.Target != log.Target)
        {
            log.Target = goap.Target;
            Add(log, $"target {ToPrettyString(goap.Target)}");
        }

        if (goap.ForcedGoal != log.ForcedGoal)
        {
            log.ForcedGoal = goap.ForcedGoal;
            Add(log, $"reflex forces {goap.ForcedGoal?.Id ?? "nothing"}");
        }

        if (!TryComp(uid, out CFGOAPSteeringComponent? steering))
            return;

        if (steering.Obstacle != log.Obstacle)
        {
            log.Obstacle = steering.Obstacle;
            Add(log, $"obstacle {ToPrettyString(steering.Obstacle)}");
        }

        if (steering.NoWayUntil != log.NoWayUntil)
        {
            log.NoWayUntil = steering.NoWayUntil;
            Add(log, $"gave up, no way until {Seconds(steering.NoWayUntil)}");
        }

        if (steering.Route.Count != log.Route)
        {
            log.Route = steering.Route.Count;
            Add(log, $"route {steering.Route.Count} tiles");
        }
    }

    private void Add(CFGOAPDecisionLogComponent log, string entry)
    {
        if (log.Entries.Count >= CFGOAPDecisionLogComponent.MaxEntries)
            log.Entries.RemoveAt(0);

        log.Entries.Add($"{Seconds(_timing.CurTime)} {entry}");
    }

    private static string Seconds(TimeSpan time)
    {
        return time.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
    }
}
