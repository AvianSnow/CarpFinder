using Robust.Shared.Prototypes;
using Robust.Shared.Map;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Actions;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>
/// Casts spells from the radial wheel and hotbar. It pays for the cast (spell slot, volley bookkeeping),
/// then hands the action to <see cref="CFEffectSystem"/>, which resolves targets, checks, and effects.
/// </summary>
public sealed partial class CFSpellSystem : EntitySystem
{
    [Dependency] private CFActivitySystem _activities = default!;
    [Dependency] private CFPsiSystem _psi = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actionDelays = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFSpellshapeSystem _spellshapes = default!;
    [Dependency] private CFVfxSystem _vfx = default!;
    [Dependency] private CFReactionSystem _reactions = default!;
    [Dependency] private Robust.Shared.Prototypes.IPrototypeManager _prototypes = default!;
    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobState = default!;

    [SubscribeLocalEvent]
    private void OnInstant(Entity<CFSpellComponent> spell, ref CFSpellCastInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer || !TrySpendCast(args.Performer, spell))
            return;

        _psi.NoteCast(args.Performer, spell);

        // An emanation (bless) centers on the caster. Anything else affects the caster.
        if (HasComp<CFAreaEffectComponent>(spell))
            _effects.ResolveArea(spell, args.Performer, _transform.GetMapCoordinates(args.Performer));
        else
            _effects.ResolveTarget(spell, args.Performer, args.Performer);

        KeepGoing(args.Performer, spell, null);
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

        if (!TrySpendCast(args.Performer, spell))
            return;

        _psi.NoteCast(args.Performer, spell);
        _effects.DeliverToTarget(spell, args.Performer, args.Target);
        KeepGoing(args.Performer, spell, null, args.Target);
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

        if (TryMoveSustained(args.Performer, spell, aim))
            return;

        if (!TrySpendCast(args.Performer, spell))
            return;

        _psi.NoteCast(args.Performer, spell);
        _effects.ResolveArea(spell, args.Performer, aim);
        KeepGoing(args.Performer, spell, aim);
        FinishCast(args.Performer);
    }

    /// <summary>
    /// Casting a running spell again that can be moved: it goes to the new spot and nothing is paid. The damage there comes
    /// with its next pulse, so moving it over and over does not burn anything more often.
    /// </summary>
    private bool TryMoveSustained(EntityUid caster, Entity<CFSpellComponent> spell, MapCoordinates aim)
    {
        if (!spell.Comp.Sustained
            || !spell.Comp.MovesWhenRecast
            || !TryComp(caster, out CFSustainedCastsComponent? casts)
            || casts.Casts.Find(cast => cast.Spell == spell.Comp.Spell) is not { } running)
        {
            return false;
        }

        running.Aim = aim;
        _popup.PopupEntity(Loc.GetString("cf-spell-sustain-moved", ("spell", running.Name)), caster, caster);
        return true;
    }

    /// <summary>The creature has this sustained spell running. The spellbook's copy is what a client sees.</summary>
    public bool IsSustaining(EntityUid caster, ProtoId<CFSpellPrototype> spell)
    {
        if (TryComp(caster, out CFSpellbookComponent? book))
            return CFSustain.State(book, spell, _timing.CurTime) == CFSustainState.Running;

        return TryComp(caster, out CFSustainedCastsComponent? casts) && casts.Casts.Exists(cast => cast.Spell == spell);
    }

    /// <summary>A sustained spell has been cast: from now on it runs again by itself. See <see cref="CFSustain"/>.</summary>
    private void KeepGoing(EntityUid caster, Entity<CFSpellComponent> spell, MapCoordinates? aim, EntityUid? target = null)
    {
        if (!spell.Comp.Sustained)
            return;

        var now = _timing.CurTime;
        if (TryComp(caster, out CFSpellbookComponent? book))
        {
            CFSustain.Start(book, spell.Comp.Spell, now);
            Dirty(caster, book);
        }

        var level = CompOrNull<CFCombatantComponent>(caster)?.Level ?? 1;
        var casts = EnsureComp<CFSustainedCastsComponent>(caster);
        casts.Casts.RemoveAll(old => old.Spell == spell.Comp.Spell);
        casts.Casts.Add(new CFSustainedCast
        {
            Action = spell,
            Spell = spell.Comp.Spell,
            Name = MetaData(spell).EntityName,
            Aim = aim,
            Target = target,
            Next = now + CFSustain.Pulse,
            Ends = now + CFSustain.Longest,
            Rank = spell.Comp.Focus || spell.Comp.Rank == 0 ? CFCasting.CantripRank(level) : Math.Max(1, spell.Comp.ResolvedRank),
            Dc = CFCasting.Spellcasting(_prototypes, CompOrNull<CFCombatantComponent>(caster), CompOrNull<CFAbilityScoreComponent>(caster)).Dc,
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFSustainedCastsComponent>();
        while (query.MoveNext(out var caster, out var casts))
        {
            for (var i = casts.Casts.Count - 1; i >= 0; i--)
            {
                var cast = casts.Casts[i];
                if (now >= cast.Ends || TerminatingOrDeleted(cast.Action) || !_mobState.IsAlive(caster))
                {
                    casts.Casts.RemoveAt(i);
                    continue;
                }

                if (now < cast.Next)
                    continue;

                cast.Next = now + CFSustain.Pulse;
                Pulse(caster, cast);
            }

            if (casts.Casts.Count == 0)
                RemCompDeferred<CFSustainedCastsComponent>(caster);
        }
    }

    /// <summary>Runs a sustained spell's effects again, for nothing: on the caster, around the caster, or where it was aimed.</summary>
    private void Pulse(EntityUid caster, CFSustainedCast cast)
    {
        if (cast.Target is { } target)
        {
            // A spell on one creature tries it again, for as long as that creature is about.
            if (!TerminatingOrDeleted(target) && _mobState.IsAlive(target))
                _effects.ResolveTarget(cast.Action, caster, target);

            return;
        }

        if (!TryComp(cast.Action, out CFAreaEffectComponent? area))
        {
            _effects.ResolveTarget(cast.Action, caster, caster);
            return;
        }

        var here = _transform.GetMapCoordinates(caster);
        _effects.ResolveArea(cast.Action, caster, cast.Aim is { } aim && area.Shape != CFAreaShape.Emanation ? aim : here);
    }

    /// <summary>Ends a sustained spell before its time, as a successful dispel does.</summary>
    public void EndSustained(EntityUid caster, CFSustainedCast cast)
    {
        if (TryComp(caster, out CFSustainedCastsComponent? casts))
            casts.Casts.Remove(cast);

        if (TryComp(caster, out CFSpellbookComponent? book))
        {
            CFSustain.End(book, cast.Spell);
            Dirty(caster, book);
        }
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
            args.Rank = spell.Comp.ResolvedRank;
            return;
        }

        var level = CompOrNull<CFCombatantComponent>(args.User)?.Level ?? 1;
        args.Rank = CFCasting.CantripRank(level);
    }

    /// <summary>
    /// A mode picked from the menu that covers an area around the caster, with no click: Heal and Harm's 3-action
    /// emanation. Modes that do something else on pick belong to their own systems (Prestidigitation, battle forms).
    /// </summary>
    [SubscribeLocalEvent]
    private void OnModeImmediate(Entity<CFSpellComponent> spell, ref CFModeImmediateCastEvent args)
    {
        if (!TryComp(spell, out CFModeChoiceComponent? mode)
            || mode.ChosenOption() is not { AreaShape: not null } chosen
            || !CFModeRequirements.Met(EntityManager, args.User, chosen))
        {
            return;
        }

        if (!_net.IsServer || !TrySpendCast(args.User, spell))
            return;

        _psi.NoteCast(args.User, spell);
        _effects.ResolveModeArea(spell, args.User, chosen, _transform.GetMapCoordinates(args.User));
        FinishCast(args.User);
        args.Success = true;
    }

    private void CastMode(Entity<CFSpellComponent> spell, CFModeChoiceComponent mode, ref CFSpellCastTargetEvent args)
    {
        if (mode.ChosenOption() is { } chosen && !CFModeRequirements.Met(EntityManager, args.Performer, chosen))
        {
            args.Handled = true;
            return;
        }

        // A mode with an area aimed by the click: a cone toward the creature (Directed Channel).
        if (mode.ChosenOption() is { AreaShape: not null } aimed)
        {
            args.Handled = true;
            if (!_net.IsServer || !TrySpendCast(args.Performer, spell))
                return;

            _psi.NoteCast(args.Performer, spell);
            _effects.ResolveModeArea(spell, args.Performer, aimed, _transform.GetMapCoordinates(args.Target));
            FinishCast(args.Performer);
            return;
        }

        if (!_effects.InRange(spell, args.Performer, args.Target))
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-spell-out-of-range"), args.Performer, args.Performer);
            return;
        }

        var trick = new CFModeTargetEvent(args.Performer, args.Target, mode.Chosen);
        RaiseLocalEvent(spell, ref trick);

        // No system owns the mode, so it is a normal cast whose effects read the chosen mode (Command).
        if (!trick.Handled)
        {
            args.Handled = true;
            if (!_net.IsServer || !CanCastAt(spell, args.Performer, args.Target) || !TrySpendCast(args.Performer, spell))
                return;

            _psi.NoteCast(args.Performer, spell);
            _effects.DeliverToTarget(spell, args.Performer, args.Target);
            FinishCast(args.Performer);
            return;
        }

        if (!trick.Success || !TrySpendCast(args.Performer, spell))
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
            if (!TrySpendCast(args.Performer, spell))
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

    /// <summary>Pays for the cast and shows it, unless the spell is being cast subtly.</summary>
    private bool TrySpendCast(EntityUid caster, Entity<CFSpellComponent> spell)
    {
        if (!TrySpend(caster, spell.Comp))
            return false;

        if (!_spellshapes.IsSubtle(caster))
        {
            _vfx.Cast(caster, spell);

            // An enemy watching may counter it. The slot is gone either way, as it is in the rules.
            if (_reactions.TryCounterspell(caster, spell.Comp))
                return false;
        }

        NoteBespell(caster, spell.Comp);
        _spellshapes.NoteCast(caster, spell.Comp);
        return true;
    }

    /// <summary>How long after a spell a Strike still carries its energy. The rules say until the end of the turn.</summary>
    public static readonly TimeSpan BespellLasts = TimeSpan.FromSeconds(6);

    /// <summary>A spell was cast, so a character with Bespell Strikes or the like has its next Strike charged.</summary>
    public void NoteBespell(EntityUid caster, CFSpellComponent spell)
    {
        if (!TryComp(caster, out CFCombatOptionsComponent? options) || options.BespellDie <= 0)
            return;

        var cantrip = spell.Rank == 0 && !spell.Focus;
        if (cantrip && !options.BespellCantrips)
            return;

        options.BespellUntil = _timing.CurTime + BespellLasts;
    }

    public bool TrySpend(EntityUid caster, CFSpellComponent spell)
    {
        spell.CastRank = 0;
        var wanted = spell.ChosenRank;
        spell.ChosenRank = 0;
        if (HasComp<CFBattleFormComponent>(caster))
        {
            _popup.PopupEntity(Loc.GetString("cf-battle-form-no-spells"), caster, caster);
            return false;
        }

        // A sustained spell that is still running keeps itself going. Casting it again waits until it has ended.
        if (spell.Sustained && !spell.MovesWhenRecast && IsSustaining(caster, spell.Spell))
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-sustain-wait"), caster, caster);
            return false;
        }

        if (spell.Focus)
        {
            if (!TryComp<CFSpellbookComponent>(caster, out var pool))
            {
                _popup.PopupEntity(Loc.GetString("cf-spell-no-focus"), caster, caster);
                return false;
            }


            if (CFFocusSystem.Remaining(pool) <= 0)
            {
                _popup.PopupEntity(Loc.GetString("cf-spell-no-focus"), caster, caster);
                return false;
            }

            pool.FocusSpent++;
            if (spell.Conflux)
                _activities.RechargeSpellstrike(caster);

            Dirty(caster, pool);
            return true;
        }

        if (TryComp<CFSpellbookComponent>(caster, out var attuned) && CFApparitions.UsesSlot(attuned, spell.Spell, spell.Rank))
        {
            if (CFApparitions.Left(attuned, spell.Rank) <= 0)
            {
                _popup.PopupEntity(Loc.GetString("cf-spell-no-slots"), caster, caster);
                return false;
            }

            CFApparitions.Spend(attuned, spell.Rank);
            Dirty(caster, attuned);
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
            spell.CastRank = CFPreparedSpells.Spend(book, spell.Spell, spell.Rank, wanted);
            Dirty(caster, book);
            return true;
        }

        spell.CastRank = CFPreparedSpells.SpontaneousRank(book, spell.Spell, spell.Rank, wanted);
        while (book.Expended.Count <= spell.CastRank)
        {
            book.Expended.Add(0);
        }

        book.Expended[spell.CastRank]++;
        Dirty(caster, book);
        return true;
    }

    /// <summary>True when this cast can spend one slot of the spell's rank. Cantrips and creatures without a book always can.</summary>
    public bool HasSlot(EntityUid caster, CFSpellComponent spell)
    {
        // A battle form cannot cast.
        if (HasComp<CFBattleFormComponent>(caster))
            return false;

        // A running spell that can be moved stays usable, to move it.
        if (spell.Sustained && IsSustaining(caster, spell.Spell))
            return spell.MovesWhenRecast;

        if (spell.Focus)
        {
            if (!TryComp<CFSpellbookComponent>(caster, out var pool))
                return false;


            return CFFocusSystem.Remaining(pool) > 0;
        }

        if (TryComp<CFSpellbookComponent>(caster, out var attuned) && CFApparitions.UsesSlot(attuned, spell.Spell, spell.Rank))
            return CFApparitions.Left(attuned, spell.Rank) > 0;

        if (spell.Rank <= 0 || !TryComp<CFSpellbookComponent>(caster, out var book))
            return true;

        if (spell.Rank >= book.Slots.Count || book.Slots[spell.Rank] <= 0)
            return true;

        if (book.Prepared)
            return CFPreparedSpells.CanCast(book, spell.Spell, spell.Rank);

        return CFPreparedSpells.SpontaneousRank(book, spell.Spell, spell.Rank) > 0;
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
