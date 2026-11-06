using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Follows the trail of what it is after.</summary>
public sealed partial class CFGOAPFollowScentStepSystem : CFGOAPStepSystem
{
    protected override CFGOAPStep Kind => CFGOAPStep.FollowScent;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        var trail = new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.FieldFollow };
        Steering.Steer(ent, Steering.WithHabits(Goap.Profile(ent), trail), ent.Comp.Target, felt: CFGOAPFeltMobs.Chase);
    }
}
