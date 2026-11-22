using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server.CF.GOAP;

/// <summary>
/// The bridge from HTN. It owns plan, execute and replan for as long as it runs, so a root task can be GOAP alone,
/// or HTN with GOAP inside one branch. Stock HTN still handles waking and sleeping the NPC.
/// </summary>
public sealed partial class CFGOAPOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    private CFGOAPSystem _goap = default!;

    public override void Initialize(IDependencyCollection deps)
    {
        base.Initialize(deps);
        _goap = _entities.System<CFGOAPSystem>();
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        if (TryBrain(blackboard, out var brain))
            _goap.Start(brain);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!TryBrain(blackboard, out var brain))
            return HTNOperatorStatus.Failed;

        _goap.Tick(brain);
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        if (TryBrain(blackboard, out var brain))
            _goap.Shutdown(brain);
    }

    private bool TryBrain(NPCBlackboard blackboard, out Entity<CFGOAPComponent> brain)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entities.TryGetComponent(owner, out CFGOAPComponent? goap))
        {
            brain = (owner, goap);
            return true;
        }

        brain = default;
        return false;
    }
}
