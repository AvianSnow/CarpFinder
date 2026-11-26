using Content.Server.CF.GOAP.Combat;
using Content.Shared.CF.GOAP;
using Content.Shared.CombatMode;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// Lets the GOAP melee loop swing (<see cref="CFGOAPCombatSystem"/>), which CF turns into Strikes, while it keeps in
/// reach. It stands while the target stands and walks with them when they move, so it can swing on the move instead of
/// waiting for the next think to chase.
/// </summary>
public sealed partial class CFGOAPStrikeStepSystem : CFGOAPStepSystem
{
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private Content.Shared.CF.Combat.CFMonsterAttackSystem _attacks = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Strike;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (ent.Comp.Target is not { } target)
            return;

        // A creature with several attacks takes the best one that reaches this target.
        _attacks.PickFor(ent.Owner, target);
        _combatMode.SetInCombatMode(ent, true);
        EnsureComp<CFGOAPMeleeComponent>(ent).Target = target;
        Steering.Engage(ent, target);
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        // The target moves in and out of reach of a tail or a tongue, so the choice is looked at again as it fights.
        _attacks.PickFor(ent.Owner, ent.Comp.Target);
        return false;
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        RemComp<CFGOAPMeleeComponent>(ent);
    }
}
