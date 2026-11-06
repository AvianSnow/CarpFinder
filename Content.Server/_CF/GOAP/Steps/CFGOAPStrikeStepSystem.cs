using Content.Server.NPC.Components;
using Content.Shared.CF.GOAP;
using Content.Shared.CombatMode;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>Stays near and lets stock melee swing, which CF turns into Strikes.</summary>
public sealed partial class CFGOAPStrikeStepSystem : CFGOAPStepSystem
{
    [Dependency] private SharedCombatModeSystem _combatMode = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Strike;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is not { } target)
            return;

        _combatMode.SetInCombatMode(ent, true);
        EnsureComp<NPCMeleeCombatComponent>(ent).Target = target;
        Steering.Hold(ent);
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        RemComp<NPCMeleeCombatComponent>(ent);
    }
}
