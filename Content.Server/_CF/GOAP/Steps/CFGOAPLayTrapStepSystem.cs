using Content.Shared.CF.Actions;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Hazards;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Stands still until it has the actions, then builds a trap between itself and the target.</summary>
public sealed partial class CFGOAPLayTrapStepSystem : CFGOAPStepSystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFTrapLayerSystem _traps = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.LayTrap;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        Steering.Hold(ent);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryComp(ent, out CFTrapLayerComponent? layer)
            || ent.Comp.Target is not { } target
            || TerminatingOrDeleted(target))
            return true;

        if (!_actions.CanAffordActions(ent, layer.Actions))
            return false;

        var toTarget = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(ent);
        if (_traps.TryFindSpot(ent, toTarget, layer.SpacingTiles, out var spot) && _actions.TrySpendActions(ent, layer.Actions))
            _traps.TryLay((ent, layer), spot);

        return true;
    }
}
