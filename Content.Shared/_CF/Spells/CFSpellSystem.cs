using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Shared.CF.Spells;

/// <summary>
/// Casts spells from the radial wheel and hotbar. It pays for the cast (spell slot, volley bookkeeping),
/// then hands the action to <see cref="CFEffectSystem"/>, which resolves targets, checks, and effects.
/// </summary>
public sealed partial class CFSpellSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actionDelays = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFSpellshapeSystem _spellshapes = default!;

    [SubscribeLocalEvent]
    private void OnInstant(Entity<CFSpellComponent> spell, ref CFSpellCastInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer || !TrySpend(args.Performer, spell.Comp))
            return;

        // An emanation (bless) centers on the caster. Anything else affects the caster.
        if (HasComp<CFAreaEffectComponent>(spell))
            _effects.ResolveArea(spell, args.Performer, _transform.GetMapCoordinates(args.Performer));
        else
            _effects.ResolveTarget(spell, args.Performer, args.Performer);

        FinishCast(args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnTarget(Entity<CFSpellComponent> spell, ref CFSpellCastTargetEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp(spell, out CFModeChoiceComponent? mode) && mode.Chosen != 0)
        {
            CastMode(spell, mode, ref args);
            return;
        }

        if (TryComp(spell, out CFProjectileDeliveryComponent? bolts) && bolts.IsVolley)
        {
            CastVolleyShot(spell, bolts, ref args);
            return;
        }

        args.Handled = true;
        if (!_net.IsServer || !CanCastAt(spell, args.Performer, args.Target))
            return;

        if (!TrySpend(args.Performer, spell.Comp))
            return;

        _effects.DeliverToTarget(spell, args.Performer, args.Target);
        FinishCast(args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnArea(Entity<CFSpellComponent> spell, ref CFSpellCastAreaEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        var aim = _transform.ToMapCoordinates(args.Target);
        if (!_effects.ValidateArea(spell, args.Performer, aim, out var failure))
        {
            if (failure != null)
                _popup.PopupEntity(Loc.GetString(failure), args.Performer, args.Performer);
            return;
        }

        if (!TrySpend(args.Performer, spell.Comp))
            return;

        _effects.ResolveArea(spell, args.Performer, aim);
        FinishCast(args.Performer);
    }

    /// <summary>A volley stays aimed until every bolt has been clicked, so the action delay is lifted.</summary>
    [SubscribeLocalEvent]
    private void OnVolleyPerformed(Entity<CFProjectileDeliveryComponent> bolts, ref ActionPerformedEvent args)
    {
        if (!bolts.Comp.IsVolley || bolts.Comp.VolleyLeft <= 0)
            return;

        _actionDelays.RemoveCooldown(bolts.Owner);
    }

    /// <summary>Cantrips resolve at half the caster's level, rounded up. Slotted spells at their slot rank.</summary>
    [SubscribeLocalEvent]
    private void OnContext(Entity<CFSpellComponent> spell, ref CFEffectContextEvent args)
    {
        if (spell.Comp.Rank > 0)
        {
            args.Rank = spell.Comp.Rank;
            return;
        }

        var level = CompOrNull<CFCombatantComponent>(args.User)?.Level ?? 1;
        args.Rank = CFCasting.CantripRank(level);
    }

    private void CastMode(Entity<CFSpellComponent> spell, CFModeChoiceComponent mode, ref CFSpellCastTargetEvent args)
    {
        if (!_effects.InRange(spell, args.Performer, args.Target))
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-spell-out-of-range"), args.Performer, args.Performer);
            return;
        }

        var trick = new CFModeTargetEvent(args.Performer, args.Target, mode.Chosen);
        RaiseLocalEvent(spell, ref trick);
        if (!trick.Success || !TrySpend(args.Performer, spell.Comp))
            return;

        args.Handled = true;
        if (_net.IsServer)
            FinishCast(args.Performer);
    }

    /// <summary>
    /// One click of a volley. The opening click pays the slot and stores the chosen count.
    /// Each click fires one bolt, so the bolts can go to different creatures.
    /// </summary>
    private void CastVolleyShot(
        Entity<CFSpellComponent> spell,
        CFProjectileDeliveryComponent bolts,
        ref CFSpellCastTargetEvent args)
    {
        if (!_net.IsServer)
        {
            args.Handled = true;
            return;
        }

        if (!CanCastAt(spell, args.Performer, args.Target))
            return;

        var opening = false;
        if (bolts.PayThisShot)
        {
            if (!TrySpend(args.Performer, spell.Comp))
                return;

            bolts.VolleyLeft = bolts.ClampedBolts;
            opening = true;
        }
        else if (bolts.VolleyLeft <= 0)
        {
            return;
        }

        _effects.LaunchBolts(spell, args.Performer, args.Target, 1);
        bolts.VolleyLeft--;
        args.Handled = true;
        if (opening)
            FinishCast(args.Performer);
    }

    private bool CanCastAt(EntityUid spell, EntityUid caster, EntityUid target)
    {
        if (!_effects.CanTarget(spell, caster, target))
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-no-target"), caster, caster);
            return false;
        }

        if (!_effects.InRange(spell, caster, target))
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-out-of-range"), caster, caster);
            return false;
        }

        return true;
    }

    /// <summary>Casting ends invisibility and provokes reactive strikes.</summary>
    private void FinishCast(EntityUid caster)
    {
        _conditions.NoteAttack(caster);
        if (!_spellshapes.IsSubtle(caster))
            _strikes.ReactiveStrike(caster);

        _spellshapes.Consume(caster);
    }

    private bool TrySpend(EntityUid caster, CFSpellComponent spell)
    {
        if (spell.Focus)
        {
            if (!TryComp<CFSpellbookComponent>(caster, out var pool) || CFFocusSystem.Remaining(pool) <= 0)
            {
                _popup.PopupEntity(Loc.GetString("cf-spell-no-focus"), caster, caster);
                return false;
            }

            pool.FocusSpent++;
            Dirty(caster, pool);
            return true;
        }

        if (!HasSlot(caster, spell))
        {
            if (spell.Rank > 0)
                _popup.PopupEntity(Loc.GetString("cf-spell-no-slots"), caster, caster);
            return false;
        }

        if (spell.Rank <= 0 || !TryComp<CFSpellbookComponent>(caster, out var book))
            return true;

        if (spell.Rank >= book.Slots.Count || book.Slots[spell.Rank] <= 0)
            return true;

        if (book.Prepared)
        {
            CFPreparedSpells.Spend(book, spell.Spell, spell.Rank);
            Dirty(caster, book);
            return true;
        }

        while (book.Expended.Count <= spell.Rank)
        {
            book.Expended.Add(0);
        }

        book.Expended[spell.Rank]++;
        Dirty(caster, book);
        return true;
    }

    /// <summary>True when this cast can spend one slot of the spell's rank. Cantrips and creatures without a book always can.</summary>
    public bool HasSlot(EntityUid caster, CFSpellComponent spell)
    {
        if (spell.Focus)
            return TryComp<CFSpellbookComponent>(caster, out var pool) && CFFocusSystem.Remaining(pool) > 0;

        if (spell.Rank <= 0 || !TryComp<CFSpellbookComponent>(caster, out var book))
            return true;

        if (spell.Rank >= book.Slots.Count || book.Slots[spell.Rank] <= 0)
            return true;

        if (book.Prepared)
            return CFPreparedSpells.CanCast(book, spell.Spell, spell.Rank);

        var spent = spell.Rank < book.Expended.Count ? book.Expended[spell.Rank] : 0;
        return spent < book.Slots[spell.Rank];
    }

    /// <summary>Burns one slot of a rank without casting a spell, for an ability such as Spellstrike. False if none is left.</summary>
    public bool TrySpendAnySlot(EntityUid caster, int rank)
    {
        if (!TryComp<CFSpellbookComponent>(caster, out var book) || rank >= book.Slots.Count || book.Slots[rank] <= 0)
            return false;

        if (book.Prepared)
        {
            if (!CFPreparedSpells.TrySpendAny(book, rank))
                return false;

            Dirty(caster, book);
            return true;
        }

        var spent = rank < book.Expended.Count ? book.Expended[rank] : 0;
        if (spent >= book.Slots[rank])
            return false;

        while (book.Expended.Count <= rank)
            book.Expended.Add(0);

        book.Expended[rank]++;
        Dirty(caster, book);
        return true;
    }
}
