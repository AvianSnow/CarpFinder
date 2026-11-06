using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Drifts around, as erratically as its personality says.</summary>
public sealed partial class CFGOAPWanderStepSystem : CFGOAPStepSystem
{
    protected override CFGOAPStep Kind => CFGOAPStep.Wander;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        var profile = Goap.Profile(ent);
        var wander = new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.Wander, Jitter = profile.WanderJitter };
        Steering.Steer(ent, Steering.WithHabits(profile, wander));
    }
}
