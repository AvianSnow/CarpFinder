using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Demoralize, Trip, and the other activities on the action wedge.
/// Class features and chosen feats are the same kind of action, granted at spawn.
/// </summary>
public sealed partial class CFActivitySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CFActivityComponent, CFActivityInstantEvent>(OnInstant);
        SubscribeLocalEvent<CFActivityComponent, CFActivityTargetEvent>(OnTarget);
        SubscribeLocalEvent<CFActivityComponent, ActionAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<CFSpellComponent, ActionAttemptEvent>(OnSpellAttempt);
    }

    private void OnSpellAttempt(Entity<CFSpellComponent> spell, ref ActionAttemptEvent args)
    {
        if (!TryComp(args.User, out CFCombatOptionsComponent? opt) || !opt.Raging)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-act-rage-concentrate");
    }

    private void OnAttempt(Entity<CFActivityComponent> ent, ref ActionAttemptEvent args)
    {
        switch (ent.Comp.Kind)
        {
            case CFActivityKind.RaiseShield when !TryShield(args.User, out _):
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-act-no-shield");
                break;
            case CFActivityKind.Finisher
                when !TryComp(args.User, out CFCombatOptionsComponent? style) || !style.Panache:
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-act-no-panache");
                break;
            case CFActivityKind.Rage
                when !TryComp(args.User, out CFCombatOptionsComponent? rage) || !rage.CanRage:
                args.Cancelled = true;
                args.Reason = Loc.GetString("cf-act-cannot");
                break;
        }
    }

    private void OnInstant(Entity<CFActivityComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        switch (ent.Comp.Kind)
        {
            case CFActivityKind.RaiseShield:
                if (TryShield(args.Performer, out _))
                    _conditions.RaiseShield(args.Performer);
                break;
            case CFActivityKind.Rage:
                ToggleRage(args.Performer);
                break;
            case CFActivityKind.PointBlank:
                TogglePointBlank(args.Performer);
                break;
            case CFActivityKind.Inspire:
                Inspire(args.Performer);
                break;
        }
    }

    private void OnTarget(Entity<CFActivityComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        if (_conditions.IsCalmed(args.Performer)
            && ent.Comp.Kind is not CFActivityKind.Demoralize and not CFActivityKind.Feint)
        {
            _popup.PopupEntity(Loc.GetString("cf-cond-calmed"), args.Performer, args.Performer);
            return;
        }

        switch (ent.Comp.Kind)
        {
            case CFActivityKind.Demoralize:
                _conditions.TryDemoralize(args.Performer, args.Target);
                break;
            case CFActivityKind.Trip:
                Trip(args.Performer, args.Target);
                break;
            case CFActivityKind.Shove:
                Shove(args.Performer, args.Target);
                break;
            case CFActivityKind.Grapple:
                Grapple(args.Performer, args.Target);
                break;
            case CFActivityKind.Feint:
                _conditions.TryFeint(args.Performer, args.Target);
                break;
            case CFActivityKind.HuntPrey:
                Mark(args.Performer, args.Target, prey: true);
                break;
            case CFActivityKind.Exploit:
                Mark(args.Performer, args.Target, prey: false);
                break;
            case CFActivityKind.Stratagem:
                Stratagem(args.Performer, args.Target);
                break;
            case CFActivityKind.Flurry:
                Flurry(args.Performer, args.Target);
                break;
            case CFActivityKind.Spellstrike:
                Spellstrike(args.Performer, args.Target);
                break;
            case CFActivityKind.Finisher:
                Finisher(args.Performer, args.Target);
                break;
            case CFActivityKind.DoubleSlice:
                DoubleSlice(args.Performer, args.Target);
                break;
            case CFActivityKind.ExactingStrike:
                Exacting(args.Performer, args.Target);
                break;
            case CFActivityKind.SuddenCharge:
                StrikeHeld(args.Performer, args.Target, extraDice: 0, snag: false);
                break;
            case CFActivityKind.SnaggingStrike:
                StrikeHeld(args.Performer, args.Target, extraDice: 0, snag: true);
                break;
            case CFActivityKind.ViciousSwing:
                StrikeHeld(args.Performer, args.Target, extraDice: 1, snag: false);
                break;
            case CFActivityKind.TwinFeint:
                TwinFeint(args.Performer, args.Target);
                break;
            case CFActivityKind.TumbleBehind:
                Tumble(args.Performer, args.Target);
                break;
        }
    }

    private void Trip(EntityUid user, EntityUid target)
    {
        var degree = _conditions.RollSkill(user, target, "Athletics", CFAbility.Strength, CFSpellDefense.Reflex);
        if (degree is CFStrikeDegree.CriticalFailure)
        {
            _conditions.KnockProne(user);
            return;
        }

        if (degree is not (CFStrikeDegree.Success or CFStrikeDegree.CriticalSuccess))
        {
            _popup.PopupEntity(Loc.GetString("cf-act-trip-fail"), user, user);
            return;
        }

        _conditions.KnockProne(target);
        if (degree == CFStrikeDegree.CriticalSuccess)
            _conditions.SetOffGuardAll(target, 6f);
    }

    private void Shove(EntityUid user, EntityUid target)
    {
        var degree = _conditions.RollSkill(user, target, "Athletics", CFAbility.Strength, CFSpellDefense.Fortitude);
        if (degree is not (CFStrikeDegree.Success or CFStrikeDegree.CriticalSuccess))
        {
            _popup.PopupEntity(Loc.GetString("cf-act-shove-fail"), user, user);
            return;
        }

        _conditions.ReleaseGrab(target);
        _conditions.Push(user, target, degree == CFStrikeDegree.CriticalSuccess ? 10 : 5);
        if (degree == CFStrikeDegree.CriticalSuccess)
            _conditions.KnockProne(target);
        else
            _popup.PopupEntity(Loc.GetString("cf-act-shove"), target);
    }

    private void Grapple(EntityUid user, EntityUid target)
    {
        var degree = _conditions.RollSkill(user, target, "Athletics", CFAbility.Strength, CFSpellDefense.Fortitude);
        if (degree == CFStrikeDegree.CriticalFailure)
        {
            _conditions.ApplyGrabbed(user, target);
            return;
        }

        if (degree is not (CFStrikeDegree.Success or CFStrikeDegree.CriticalSuccess))
        {
            _popup.PopupEntity(Loc.GetString("cf-act-grapple-fail"), user, user);
            return;
        }

        _conditions.ApplyGrabbed(target, user);
    }

    private void ToggleRage(EntityUid user)
    {
        if (!TryComp(user, out CFCombatOptionsComponent? opt) || !opt.CanRage)
            return;

        opt.Raging = !opt.Raging;
        Dirty(user, opt);
        _popup.PopupEntity(Loc.GetString(opt.Raging ? "cf-act-rage-start" : "cf-act-rage-end"), user, user);
    }

    private void TogglePointBlank(EntityUid user)
    {
        var opt = EnsureComp<CFCombatOptionsComponent>(user);
        opt.PointBlank = !opt.PointBlank;
        Dirty(user, opt);
        _popup.PopupEntity(Loc.GetString(opt.PointBlank ? "cf-act-point-blank" : "cf-act-point-blank-end"), user, user);
    }

    private void Mark(EntityUid user, EntityUid target, bool prey)
    {
        var opt = EnsureComp<CFCombatOptionsComponent>(user);
        if (prey)
        {
            opt.Prey = target;
            opt.PreyHitThisTurn = false;
            _popup.PopupEntity(Loc.GetString("cf-act-hunt-prey"), user, user);
        }
        else
        {
            opt.Exploited = target;
            _conditions.SetOffGuardTo(target, user, 60f);
            _popup.PopupEntity(Loc.GetString("cf-act-exploit"), user, user);
        }

        Dirty(user, opt);
    }

    private void Stratagem(EntityUid user, EntityUid target)
    {
        var opt = EnsureComp<CFCombatOptionsComponent>(user);
        opt.StratagemRoll = _random.Next(1, 21);
        opt.StratagemTarget = target;
        Dirty(user, opt);
        _popup.PopupEntity(Loc.GetString("cf-act-stratagem", ("roll", opt.StratagemRoll)), user, user);
    }

    private void Flurry(EntityUid user, EntityUid target)
    {
        var stats = _strikes.UnarmedStats(user);
        _strikes.PerformStrike(user, target, stats, user, melee: true);
        _strikes.PerformStrike(user, target, stats, user, melee: true);
    }

    private void Spellstrike(EntityUid user, EntityUid target)
    {
        if (!_strikes.TryMeleeWeapon(user, out var stats, out var weapon))
            return;

        var dice = 1;
        var die = 8;
        if (TryComp(user, out CFSpellbookComponent? book) && book.Slots.Count > 1 && book.Slots[1] > 0)
        {
            var spent = book.Expended.Count > 1 ? book.Expended[1] : 0;
            if (spent < book.Slots[1])
            {
                while (book.Expended.Count <= 1)
                    book.Expended.Add(0);

                book.Expended[1]++;
                Dirty(user, book);
                dice = 2;
                die = 6;
            }
        }

        var bonus = 0;
        for (var i = 0; i < dice; i++)
            bonus += _random.Next(1, die + 1);

        _strikes.PerformStrike(user, target, stats, weapon, melee: true, bonusDamage: bonus);
    }

    private void Finisher(EntityUid user, EntityUid target)
    {
        if (!TryComp(user, out CFCombatOptionsComponent? opt) || !opt.Panache)
            return;

        if (!_strikes.TryMeleeWeapon(user, out var stats, out var weapon))
            return;

        _strikes.PerformStrike(user, target, stats, weapon, melee: true, finisher: true);
        opt.Panache = false;
        Dirty(user, opt);
    }

    private void DoubleSlice(EntityUid user, EntityUid target)
    {
        if (!_strikes.TryMeleeWeapon(user, out var stats, out var weapon))
            return;

        var map = _actions.MapPenalty(user, stats.Agile);
        _strikes.PerformStrike(user, target, stats, weapon, melee: true, fixedMap: map);
        var second = stats.Agile ? 0 : -2;
        _strikes.PerformStrike(user, target, stats, weapon, melee: true, fixedMap: map, attackPenalty: second);
    }

    private void Exacting(EntityUid user, EntityUid target)
    {
        if (!_strikes.TryMeleeWeapon(user, out var stats, out var weapon))
            return;

        var hit = _strikes.PerformStrike(user, target, stats, weapon, melee: true);
        if (!hit)
            _actions.UndoAttack(user);
    }

    private void StrikeHeld(EntityUid user, EntityUid target, int extraDice, bool snag)
    {
        if (!_strikes.TryMeleeWeapon(user, out var stats, out var weapon))
            return;

        var hit = _strikes.PerformStrike(user, target, stats, weapon, melee: true, extraDice: extraDice);
        if (hit && snag)
            _conditions.SetOffGuardAll(target, 6f);
    }

    private void TwinFeint(EntityUid user, EntityUid target)
    {
        _conditions.TryFeint(user, target);
        StrikeHeld(user, target, extraDice: 0, snag: false);
    }

    private void Tumble(EntityUid user, EntityUid target)
    {
        var degree = _conditions.RollSkill(user, target, "Acrobatics", CFAbility.Dexterity, CFSpellDefense.Reflex);
        if (degree is not (CFStrikeDegree.Success or CFStrikeDegree.CriticalSuccess))
        {
            _popup.PopupEntity(Loc.GetString("cf-act-tumble-fail"), user, user);
            return;
        }

        _conditions.SetOffGuardTo(target, user, 6f);
        _popup.PopupEntity(Loc.GetString("cf-act-tumble"), user, user);
    }

    private void Inspire(EntityUid user)
    {
        Buff(user);
        var origin = _transform.GetMapCoordinates(user);
        foreach (var uid in _lookup.GetEntitiesInRange(origin, 12f))
        {
            if (uid == user || !HasComp<MobStateComponent>(uid) || _factions.CanHarm(user, uid))
                continue;

            Buff(uid);
        }

        _popup.PopupEntity(Loc.GetString("cf-act-inspire"), user, user);
    }

    private void Buff(EntityUid ally)
    {
        var status = EnsureComp<CFSpellStatusComponent>(ally);
        status.AttackBonus = Math.Max(status.AttackBonus, 1);
        status.SaveBonus = Math.Max(status.SaveBonus, 1);
        var expires = _timing.CurTime + TimeSpan.FromSeconds(12);
        if (expires > status.Expires)
            status.Expires = expires;
    }

    private bool TryShield(EntityUid user, out CFShieldComponent? shield)
    {
        shield = null;
        return _hands.TryGetActiveItem(user, out var held)
               && held != null
               && TryComp(held.Value, out shield);
    }
}
