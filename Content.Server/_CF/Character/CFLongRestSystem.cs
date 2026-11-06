using Content.Shared.Bed.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;

namespace Content.Server.CF.Character;

/// <summary>
/// Falling asleep in a bed is a long rest: hit points and spell slots come back at once.
/// No time passes, and nothing limits how often it can be done.
/// </summary>
public sealed partial class CFLongRestSystem : EntitySystem
{
    [Dependency] private CFDyingSystem _dying = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [SubscribeLocalEvent]
    private void OnSleepStateChanged(Entity<CFCombatantComponent> ent, ref SleepStateChangedEvent args)
    {
        if (args.FellAsleep && InBed(ent) && _mobState.IsAlive(ent))
            LongRest(ent);
    }

    /// <summary>A bed is whatever stock lets you sleep in: a strap with <see cref="HealOnBuckleComponent"/>.</summary>
    private bool InBed(EntityUid mob)
    {
        return TryComp(mob, out BuckleComponent? buckle)
               && buckle.BuckledTo is { } strap
               && HasComp<HealOnBuckleComponent>(strap);
    }

    public void LongRest(EntityUid mob)
    {
        _dying.RestoreFully(mob);

        if (TryComp(mob, out CFSpellbookComponent? book))
        {
            for (var rank = 0; rank < book.Expended.Count; rank++)
            {
                book.Expended[rank] = 0;
            }

            book.FocusSpent = 0;
            Dirty(mob, book);
        }

        _popup.PopupEntity(Loc.GetString("cf-long-rest"), mob, mob);
    }
}
