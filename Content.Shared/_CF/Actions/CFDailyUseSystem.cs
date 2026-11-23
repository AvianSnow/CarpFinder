using Content.Shared.Actions;
using Content.Shared.Actions.Events;

namespace Content.Shared.CF.Actions;

/// <summary>Counts the uses of an action with <see cref="CFDailyUsesComponent"/>, and refuses it once they are spent.</summary>
public sealed partial class CFDailyUseSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFDailyUsesComponent> ent, ref ActionAttemptEvent args)
    {
        if (!ent.Comp.Spent)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-act-daily-spent");
    }

    [SubscribeLocalEvent]
    private void OnPerformed(Entity<CFDailyUsesComponent> ent, ref ActionPerformedEvent args)
    {
        ent.Comp.Used++;
        Dirty(ent);
    }

    /// <summary>Gives every daily action the creature has its uses back. A long rest calls this.</summary>
    public void Restore(EntityUid mob)
    {
        // An Oracle's curse lets go.
        EntityManager.System<Content.Shared.CF.Spells.CFCurseboundSystem>().Clear(mob);

        // An Alchemist's versatile vials are all made again.
        EntityManager.System<Content.Shared.CF.Alchemy.CFQuickAlchemySystem>().Refill(mob);

        foreach (var action in _actions.GetActions(mob))
        {
            if (!TryComp(action, out CFDailyUsesComponent? daily) || daily.Used == 0)
                continue;

            daily.Used = 0;
            Dirty(action, daily);
        }
    }
}
