using Content.Shared.CF.Actions;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>A player action, through <see cref="CFManeuverSystem.TryUse"/>, so it follows the same rules.</summary>
public sealed partial class CFGOAPManeuverStepSystem : CFGOAPStepSystem
{
    [Dependency] private CFManeuverSystem _maneuvers = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Maneuver;

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        _maneuvers.TryUse(ent, action.Maneuver, ent.Comp.Target);
        return true;
    }
}
