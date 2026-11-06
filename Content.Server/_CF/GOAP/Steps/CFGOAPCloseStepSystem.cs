using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Gets within reach of the target.</summary>
public sealed partial class CFGOAPCloseStepSystem : CFGOAPStepSystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Close;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is { } quarry)
            Steering.Chase(ent, quarry);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is { } quarry && !TerminatingOrDeleted(quarry))
            Steering.Navigate(ent, _transform.GetWorldPosition(quarry), quarry);

        return CFGOAPSystem.Knows(ent.Comp, CFGOAPFact.TargetInReach);
    }
}
