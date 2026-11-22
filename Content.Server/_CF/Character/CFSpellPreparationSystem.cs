using Content.Shared.CF.Character;
using Content.Shared.CF.Spells;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.Character;

/// <summary>Stores what a prepared caster wants to prepare. It comes from a client, so it is checked here.</summary>
public sealed partial class CFSpellPreparationSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

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

        // A lower rank spell can go in a higher slot, where it is heightened. The other way round cannot happen.
        foreach (var slot in plan)
        {
            if (!_prototypes.TryIndex(slot.Spell, out var spell) || spell.Rank > slot.Rank)
                return false;
        }

        caster.Comp.PlannedSlots = new List<CFPreparedSlot>(plan);
        Dirty(caster);
        return true;
    }
}
