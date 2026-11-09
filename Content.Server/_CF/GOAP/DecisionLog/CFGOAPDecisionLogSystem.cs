using System.Linq;
using System.Text;
using Content.Server.CF.GOAP.Steering;
using Content.Server.NPC.Systems;
using Content.Shared.CF.GOAP;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.DecisionLog;

/// <summary>
/// FOR DEBUGGING ONLY. This will be removed once the AI has settled: <c>.agent/goap.md</c> lists the steps.
/// Writes down why a brain did what it did: goals, plans, steps, facts by name, reflexes, obstacles, routes and giving up.
/// It compares each brain against its last look once a tick, from the outside, so the AI itself has no logging in it.
/// Two changes in one tick show as their result, and a one-action step can finish before it is seen.
/// </summary>
/// <remarks>Do not call this folder Debug. <c>.gitignore</c> drops any folder with that name, so it would never be pushed.</remarks>
public sealed partial class CFGOAPDecisionLogSystem : EntitySystem
{
    /// <summary>Flips with every swing and every action regained, which would bury everything else.</summary>
    private const CFGOAPFact Ignored = CFGOAPFact.ActionsLeft;

    private static readonly CFGOAPFact[] AllFacts = Enum.GetValues<CFGOAPFact>();

    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(NPCSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        WatchAll();
    }

    /// <summary>Takes a look now. A test that only calls <c>Think</c> has no ticks to do this for it.</summary>
    public void WatchAll()
    {
        var query = AllEntityQuery<CFGOAPComponent>();
        while (query.MoveNext(out var uid, out var goap))
        {
            Watch(uid, goap, EnsureComp<CFGOAPDecisionLogComponent>(uid));
        }
    }

    private void Watch(EntityUid uid, CFGOAPComponent goap, CFGOAPDecisionLogComponent log)
    {
        WatchPlan(log, goap);

        if (goap.Facts != log.Facts)
        {
            var changes = FactChanges(log.Facts, goap.Facts);
            log.Facts = goap.Facts;
            if (changes.Length > 0)
                Add(log, $"facts {changes}");
        }

        if (goap.Target != log.Target)
        {
            log.Target = goap.Target;
            Add(log, $"target {ToPrettyString(goap.Target)}");
        }

        if (goap.Node != log.Node)
        {
            log.Node = goap.Node;
            if (goap.Node != null)
                Add(log, $"going to node {ToPrettyString(goap.Node)}");
        }

        if (goap.ForcedUntil != log.ForcedUntil)
        {
            log.ForcedUntil = goap.ForcedUntil;
            Add(log, $"reflex forced {goap.ForcedGoal?.Id ?? "nothing"}");
        }

        if (goap.CorneredUntil != log.CorneredUntil)
        {
            log.CorneredUntil = goap.CorneredUntil;
            Add(log, "cornered, nowhere better to run");
        }

        if (goap.HurtByUntil != log.HurtByUntil)
        {
            log.HurtByUntil = goap.HurtByUntil;
            Add(log, $"hurt by {ToPrettyString(goap.HurtBy)}");
        }

        if (TryComp(uid, out CFGOAPSteeringComponent? steering))
            WatchSteering(log, steering);
    }

    private void WatchPlan(CFGOAPDecisionLogComponent log, CFGOAPComponent goap)
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

        if (goap.Step == log.Step && goap.StepStarted == log.StepStarted)
            return;

        log.Step = goap.Step;
        log.StepStarted = goap.StepStarted;
        if (goap.StepStarted && goap.Step < goap.Plan.Count)
            Add(log, $"step {goap.Plan[goap.Step]}");
    }

    private void WatchSteering(CFGOAPDecisionLogComponent log, CFGOAPSteeringComponent steering)
    {
        if (steering.Obstacle != log.Obstacle)
        {
            if (log.Obstacle is { } old)
            {
                var gaveUp = log.ObstacleField?.GaveUp.ContainsKey(old) == true;
                Add(log, $"{(gaveUp ? "gave up on" : "done with")} {ToPrettyString(old)}");
            }

            if (steering.Obstacle is { } obstacle)
                Add(log, $"obstacle {ToPrettyString(obstacle)}");

            log.Obstacle = steering.Obstacle;
            log.ObstacleField = steering.ObstacleField;
        }

        if (steering.Working != log.Working)
        {
            log.Working = steering.Working;
            if (steering.Working)
                Add(log, "working on it");
        }

        if (steering.NoWayUntil != log.NoWayUntil)
        {
            log.NoWayUntil = steering.NoWayUntil;
            if (steering.NoWayUntil > _timing.CurTime)
                Add(log, "no other way there");
        }

        if (steering.Route.Count != log.Route)
        {
            log.Route = steering.Route.Count;
            Add(log, $"route {steering.Route.Count} tiles");
        }
    }

    private void Add(CFGOAPDecisionLogComponent log, string text)
    {
        if (log.Entries.Count >= CFGOAPDecisionLogComponent.MaxEntries)
            log.Entries.RemoveAt(0);

        // Invariant, because some machines would print 5,5 and a script could not read it.
        log.Entries.Add(FormattableString.Invariant($"{_timing.CurTime.TotalSeconds:F1}s {text}"));
    }

    /// <summary>What changed between two sets of facts, by name: <c>+TargetVisible -KnowsLkp</c>.</summary>
    public static string FactChanges(ulong before, ulong after)
    {
        var changes = new StringBuilder();
        foreach (var fact in AllFacts)
        {
            var bit = (ulong) fact;
            if (fact == Ignored || bit == 0 || (before & bit) == (after & bit))
                continue;

            if (changes.Length > 0)
                changes.Append(' ');

            changes.Append((after & bit) != 0 ? '+' : '-').Append(fact);
        }

        return changes.ToString();
    }
}
