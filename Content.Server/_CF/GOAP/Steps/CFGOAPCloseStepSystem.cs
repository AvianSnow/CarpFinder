using Content.Server.CF.GOAP.Sensors;
using Content.Server.NPC.Components;
using Content.Shared.CF.GOAP;
using Content.Shared.CombatMode;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// Gets within reach of the target, slowing down as it closes so it stops beside them instead of running past. When a
/// Strike comes next it already swings on the way in, so a creature walking off does not get away while this one
/// finishes its walk. It ends the moment the target is in reach, not at the next think.
/// </summary>
public sealed partial class CFGOAPCloseStepSystem : CFGOAPStepSystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private CFGOAPSightSensorSystem _sight = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Close;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is not { } quarry)
            return;

        Steering.Chase(ent, quarry);

        // Stock melee only swings once the target is in range, so it is safe to have it ready the whole way.
        if (Goap.NextAction(ent)?.Step == CFGOAPStep.Strike)
        {
            _combatMode.SetInCombatMode(ent, true);
            EnsureComp<NPCMeleeCombatComponent>(ent).Target = quarry;
        }
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is not { } quarry || TerminatingOrDeleted(quarry))
            return false;

        var at = _transform.GetWorldPosition(quarry);
        if (_sight.InReach(ent, quarry, _transform.GetWorldPosition(ent)))
            return true;

        Steering.Navigate(ent, at, quarry);
        return false;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        RemComp<NPCMeleeCombatComponent>(ent);
    }
}
