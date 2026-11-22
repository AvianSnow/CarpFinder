using Content.Server.CF.GOAP.Influence;
using Content.Server.CF.GOAP.Sensors;
using Content.Server.CF.GOAP.Steering;
using Content.Server.CF.GOAP.Steps;
using Content.Server.NPC.HTN;
using Content.Server.CF.GOAP.Tactics;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Spells;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP;

/// <summary>
/// Goal-oriented planning for advanced NPCs, on top of HTN. <see cref="CFGOAPOperator"/> calls <see cref="Tick"/>.
/// This only decides: sensors in <c>Sensors/</c> say what is true, and steps in <c>Steps/</c> do the work.
/// It never cheats: sensors use sight, the influence maps, the last known position, and nodes.
/// </summary>
public sealed partial class CFGOAPSystem : EntitySystem
{
    /// <summary>How near the last known position counts as there.</summary>
    public const float LkpArrival = 1f;

    /// <summary>How close to home counts as back, in tiles.</summary>
    public const float HomeArrival = 1.5f;

    private static readonly TimeSpan ThinkInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>How often it thinks while its goal is calm. Being hurt still makes it think at once.</summary>
    private static readonly TimeSpan CalmThinkInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The most steps one tick runs, as each finished step starts the next. A step that finishes as it begins could
    /// otherwise run a whole plan in one tick.
    /// </summary>
    private const int StepsPerTick = 3;

    /// <summary>Below this an action is not worth planning at all.</summary>
    private const float MinimumValue = 0.02f;

    private static readonly ProtoId<CFGOAPGoalPrototype> ObeyGoal = "Obey";
    private static readonly ProtoId<CFGOAPActionPrototype> ObeyAction = "Obey";

    [Dependency] private CFCommandSystem _command = default!;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFGOAPInfluenceMapSystem _influence = default!;
    [Dependency] private CFGOAPSteeringSystem _steering = default!;
    [Dependency] private CFGOAPTacticsSystem _tactics = default!;

    /// <summary>Filled in as the step systems start up, and fixed after that. Nothing about a creature is kept here.</summary>
    private readonly Dictionary<CFGOAPStep, CFGOAPStepSystem> _steps = new();

    /// <summary>Lets a plan reach a step system. Each one calls this once.</summary>
    public void AddStep(CFGOAPStep kind, CFGOAPStepSystem step)
    {
        _steps[kind] = step;
    }

    public bool HasStep(CFGOAPStep kind)
    {
        return _steps.ContainsKey(kind);
    }

    /// <summary>
    /// A brain always runs the creature. With two parents the first one's <c>HTN</c> wins, so a mob with stock AI of its
    /// own would otherwise keep that and never think, whatever <c>CFGOAPBrain</c> says.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBrainInit(Entity<CFGOAPComponent> ent, ref ComponentInit args)
    {
        if (TryComp(ent, out HTNComponent? htn))
            htn.RootTask = new HTNCompoundTask { Task = "CFGOAPCompound" };
    }

    /// <summary>Gets a brain ready to run.</summary>
    public void Start(Entity<CFGOAPComponent> ent)
    {
        if (Transform(ent).GridUid is { } grid)
            _influence.EnsureMap(grid);

        ent.Comp.Home ??= _transform.GetMoverCoordinates(ent);

        var steering = EnsureComp<CFGOAPSteeringComponent>(ent);
        steering.SpeedMultiplier = Profile(ent).MaxSpeed;

        // Each think is timed from the last one, so this keeps a group that woke together from thinking on one tick.
        ent.Comp.NextThink = _timing.CurTime + ThinkInterval * _random.NextDouble();
    }

    /// <summary>Where home is now. False if it has none it could walk to, such as one on another level.</summary>
    public bool TryGetHome(Entity<CFGOAPComponent> ent, out MapCoordinates home)
    {
        home = default;
        if (ent.Comp.Home is not { } coordinates || !Exists(coordinates.EntityId))
            return false;

        home = _transform.ToMapCoordinates(coordinates);
        return home.MapId == Transform(ent).MapID;
    }

    /// <summary>Stops whatever it was doing.</summary>
    public void Shutdown(Entity<CFGOAPComponent> ent)
    {
        EndStep(ent);
        ent.Comp.Plan.Clear();
        ent.Comp.Goal = null;
    }

    public void Tick(Entity<CFGOAPComponent> ent)
    {
        var now = _timing.CurTime;
        if (now >= ent.Comp.NextThink)
        {
            ent.Comp.NextThink = now + ThinkInterval;
            Think(ent);

            var calm = ent.Comp.Goal is { } goal && _prototypes.Index(goal).Calm;
            if (calm)
                ent.Comp.NextThink = now + CalmThinkInterval;

            if (TryComp(ent, out CFGOAPSteeringComponent? steering))
                steering.Calm = calm;
        }

        RunStep(ent);
    }

    public CFGOAPProfilePrototype Profile(Entity<CFGOAPComponent> ent)
    {
        return _prototypes.Index(ent.Comp.Profile);
    }

    /// <summary>Reads the world, then replans if the goal changed or a fact flipped.</summary>
    public void Think(Entity<CFGOAPComponent> ent)
    {
        var before = ent.Comp.Facts;
        var sense = new CFGOAPSenseEvent(ent, _transform.GetMapCoordinates(ent), Profile(ent));
        RaiseLocalEvent(ref sense);
        ent.Comp.Facts = (ulong) sense.Facts;
        CheckReflexes(ent);

        var goal = Obeying(ent, out var plan) ? ObeyGoal : ChooseGoal(ent, out plan);
        if (goal == ent.Comp.Goal && before == ent.Comp.Facts && ent.Comp.Step < ent.Comp.Plan.Count)
            return;

        // A plan that still starts the same way keeps running, so a fact flipping does not restart a walk.
        var sameStep = ent.Comp.Step < ent.Comp.Plan.Count
                       && plan.Count > 0
                       && ent.Comp.Plan[ent.Comp.Step] == plan[0];
        if (!sameStep)
            EndStep(ent);

        ent.Comp.Goal = goal;
        ent.Comp.Plan = plan;
        ent.Comp.Step = 0;
    }

    /// <summary>Whether a fact was true on its last think.</summary>
    public static bool Knows(CFGOAPComponent goap, CFGOAPFact fact)
    {
        return (goap.Facts & (ulong) fact) != 0;
    }

    /// <summary>Its share of hit points, from 0 to 1. Anything without hit points counts as unhurt.</summary>
    public float Health(EntityUid uid)
    {
        return TryComp(uid, out CFCombatantComponent? combat) && combat.MaxHp > 0
            ? (float) combat.CurrentHp / combat.MaxHp
            : 1f;
    }

    /// <summary>
    /// A Command order beats every goal, and is not in the goal lists, because encounters replace those.
    /// </summary>
    private bool Obeying(Entity<CFGOAPComponent> ent, out List<ProtoId<CFGOAPActionPrototype>> plan)
    {
        plan = new List<ProtoId<CFGOAPActionPrototype>>();
        if (_command.Current(ent) == CFCommandOrder.None)
            return false;

        plan.Add(ObeyAction);
        return true;
    }

    /// <summary>The best goal that a plan exists for. A forced goal comes first while it lasts.</summary>
    private ProtoId<CFGOAPGoalPrototype>? ChooseGoal(Entity<CFGOAPComponent> ent, out List<ProtoId<CFGOAPActionPrototype>> plan)
    {
        var profile = Profile(ent);
        var options = Options(ent, profile);
        var health = Health(ent);
        plan = new List<ProtoId<CFGOAPActionPrototype>>();
        ProtoId<CFGOAPGoalPrototype>? best = null;
        var bestPriority = float.MinValue;

        foreach (var id in ent.Comp.Goals)
        {
            var goal = _prototypes.Index(id);
            var priority = Priority(ent, goal, profile, health);
            if (priority <= bestPriority || !Applies(ent, goal, profile, health))
                continue;

            var conditions = CFGOAPConditions.From(goal.Wants, goal.Avoids);
            var steps = CFGOAPPlanner.Plan(ent.Comp.Facts, conditions, options);
            if (steps == null || steps.Count == 0)
                continue;

            bestPriority = priority;
            best = id;
            plan.Clear();
            foreach (var step in steps)
            {
                plan.Add(ent.Comp.Actions[step]);
            }
        }

        return best;
    }

    private float Priority(Entity<CFGOAPComponent> ent, CFGOAPGoalPrototype goal, CFGOAPProfilePrototype profile, float health)
    {
        if (ent.Comp.ForcedGoal == goal.ID && _timing.CurTime < ent.Comp.ForcedUntil)
            return float.MaxValue;

        var priority = goal.Priority + goal.WhenHurt * (1f - health);
        return priority * profile.GoalPriority.GetValueOrDefault(goal.ID, 1f);
    }

    private bool Applies(Entity<CFGOAPComponent> ent, CFGOAPGoalPrototype goal, CFGOAPProfilePrototype profile, float health)
    {
        if (ent.Comp.ForcedGoal == goal.ID && _timing.CurTime < ent.Comp.ForcedUntil)
            return true;

        if (goal.BelowFleeHealth && health >= profile.FleeHealth)
            return false;

        return CFGOAPConditions.From(goal.OnlyWhen, goal.NotWhen).Unmet(ent.Comp.Facts) == 0;
    }

    private List<CFGOAPOption> Options(Entity<CFGOAPComponent> ent, CFGOAPProfilePrototype profile)
    {
        var options = new List<CFGOAPOption>(ent.Comp.Actions.Count);
        for (var i = 0; i < ent.Comp.Actions.Count; i++)
        {
            var id = ent.Comp.Actions[i];
            var action = _prototypes.Index(id);
            var cost = action.Cost * profile.ActionCost.GetValueOrDefault(id, 1f);
            if (action.ValuedBy != CFGOAPValue.None)
            {
                // The planner takes the cheapest route, so what an action is worth has to be spoken as a cost.
                var value = _tactics.Value(ent, action);
                if (value < MinimumValue)
                    continue;

                cost /= value;
            }

            options.Add(new CFGOAPOption(
                i,
                CFGOAPConditions.From(action.Needs, action.NeedsNot),
                CFGOAPConditions.From(action.Gives, action.Removes),
                cost));
        }

        return options;
    }

    /// <summary>
    /// Runs the current step. One that finishes hands straight over to the next in the same tick, so walking up to a
    /// target and swinging at it do not have a standstill between them.
    /// </summary>
    private void RunStep(Entity<CFGOAPComponent> ent)
    {
        for (var i = 0; i < StepsPerTick && ent.Comp.Step < ent.Comp.Plan.Count; i++)
        {
            var action = _prototypes.Index(ent.Comp.Plan[ent.Comp.Step]);
            var step = _steps[action.Step];
            if (!ent.Comp.StepStarted)
            {
                ent.Comp.StepStarted = true;
                ent.Comp.StepTime = _timing.CurTime;
                step.Begin(ent, action);
            }

            if (!step.Run(ent, action))
                return;

            EndStep(ent);
            ent.Comp.Step++;
        }
    }

    /// <summary>The action after the current one, if the plan has one.</summary>
    public CFGOAPActionPrototype? NextAction(Entity<CFGOAPComponent> ent)
    {
        var next = ent.Comp.Step + 1;
        return next < ent.Comp.Plan.Count ? _prototypes.Index(ent.Comp.Plan[next]) : null;
    }

    private void EndStep(Entity<CFGOAPComponent> ent)
    {
        if (ent.Comp.StepStarted && ent.Comp.Step < ent.Comp.Plan.Count)
        {
            var action = _prototypes.Index(ent.Comp.Plan[ent.Comp.Step]);
            _steps[action.Step].End(ent, action);
        }

        ent.Comp.StepStarted = false;
        _steering.Stop(ent.Owner);
    }
}
