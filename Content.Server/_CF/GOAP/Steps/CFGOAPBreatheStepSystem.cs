using Content.Shared.CF.Actions;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// Stops, and lets its breath weapon loose at where the target stands, through the same action a player aims. Finished
/// once it has breathed, or when it cannot.
/// </summary>
public sealed partial class CFGOAPBreatheStepSystem : CFGOAPStepSystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFBreathWeaponSystem _breath = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Breathe;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        Steering.Hold(ent);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is not { } target
            || TerminatingOrDeleted(target)
            || !_breath.TryGetBreath(ent, out var breath))
            return true;

        _breath.TryBreathe(ent, breath.Value, _transform.GetMapCoordinates(target));
        return true;
    }
}
