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
    [Dependency] private Content.Shared.CF.Combat.CFMonsterShotSystem _shots = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Breathe;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        Steering.Hold(ent);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is not { } target || TerminatingOrDeleted(target))
            return true;

        if (_breath.TryGetBreath(ent, out var breath)
            && _breath.IsReady(ent, breath.Value)
            && _breath.TryBreathe(ent, breath.Value, _transform.GetMapCoordinates(target)))
        {
            return true;
        }

        // No breath to let loose: a ranged attack of its own, such as a web.
        if (_shots.TryGetReady(ent, out var shot) && _shots.Worthwhile(ent, shot, target))
            _shots.TryShoot(ent, shot, target);

        return true;
    }
}
