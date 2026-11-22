using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Moves toward nearby allies.</summary>
public sealed partial class CFGOAPRegroupStepSystem : CFGOAPStepSystem
{
    private const float AllyTiles = 6f;

    protected override CFGOAPStep Kind => CFGOAPStep.Regroup;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        var cohesion = new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.Cohesion, Radius = AllyTiles };
        Steering.Steer(ent, Steering.WithHabits(Goap.Profile(ent), cohesion));
    }
}
