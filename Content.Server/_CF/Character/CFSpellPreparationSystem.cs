using Content.Shared.CF.Character;
using Content.Shared.CF.Spells;

namespace Content.Server.CF.Character;

/// <summary>Stores what a prepared caster wants to prepare. It comes from a client, so it is checked here.</summary>
public sealed partial class CFSpellPreparationSystem : EntitySystem
{
    [SubscribeNetworkEvent]
    private void OnPlan(CFPlanPreparationEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } caster || !TryComp(caster, out CFSpellbookComponent? book))
            return;

        TrySetPlan((caster, book), ev.Slots);
    }

    public bool TrySetPlan(Entity<CFSpellbookComponent> caster, List<CFPreparedSlot> plan)
    {
        if (!caster.Comp.Prepared || !CFPreparedSpells.IsValidPlan(caster.Comp, plan))
            return false;

        caster.Comp.PlannedSlots = new List<CFPreparedSlot>(plan);
        Dirty(caster);
        return true;
    }
}
