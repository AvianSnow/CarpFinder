using Content.Shared.CF.Actions;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>A player action, through <see cref="CFManeuverSystem.TryUse"/>, so it follows the same rules.</summary>
public sealed partial class CFGOAPManeuverStepSystem : CFGOAPStepSystem
{
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFManeuverSystem _maneuvers = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Maneuver;

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        // These take the place of an attack, so they wait as a Strike does. Hide is not aimed at anyone.
        if (action.Maneuver is CFBasicAction.Demoralize or CFBasicAction.Feint)
        {
            if (_actions.StillThinking(ent))
                return false;

            _actions.FinishThinking(ent);
        }

        _maneuvers.TryUse(ent, action.Maneuver, ent.Comp.Target);
        return true;
    }
}
