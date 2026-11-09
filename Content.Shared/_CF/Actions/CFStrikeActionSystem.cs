using Content.Shared.Actions.Events;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;

namespace Content.Shared.CF.Actions;

/// <summary>Runs actions that are a Strike with something extra. See <see cref="CFStrikeActionComponent"/>.</summary>
public sealed partial class CFStrikeActionSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFTauntSystem _taunts = default!;
    [Dependency] private CFReloadSystem _reload = default!;
    [Dependency] private CFActivitySystem _activities = default!;

    /// <summary>
    /// A weapon held in both hands for one attack. One with the two-hand trait uses that die, and any other goes up one step.
    /// </summary>
    public static CFStrikeWeaponStats WithTwoHandedGrip(CFStrikeWeaponStats weapon)
    {
        var die = weapon.Traits.TwoHand > 0 ? Math.Max(weapon.DamageDie, weapon.Traits.TwoHand) : Math.Min(12, weapon.DamageDie + 2);
        return weapon with { DamageDie = die };
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFStrikeActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (args.Target is { } target && WhyNot(args.User, target, ent.Comp) is { } reason)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString(reason);
        }
    }

    [SubscribeLocalEvent]
    private void OnPerform(Entity<CFStrikeActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        // Not handled means the action costs nothing, which is right for one that could not be used.
        if (WhyNot(args.Performer, args.Target, ent.Comp) is { } reason)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString(reason), args.Performer, args.Performer);

            return;
        }

        args.Handled = true;
        if (_net.IsServer)
            Strike(ent, args.Performer, args.Target);
    }

    /// <summary>The message for why this cannot be used on that target, or null if it can.</summary>
    private string? WhyNot(EntityUid user, EntityUid target, CFStrikeActionComponent action)
    {
        if (action.PreyOnly && CompOrNull<CFCombatOptionsComponent>(user)?.Prey != target)
            return "cf-strike-action-needs-prey";

        if (action.ProneTarget && !_conditions.IsProne(target))
            return "cf-strike-action-needs-prone";

        if (action.Finisher && CompOrNull<CFCombatOptionsComponent>(user)?.Panache != true)
            return "cf-strike-action-needs-panache";

        if (action.NeedsIgnoredTaunt && !_taunts.HasIgnored(target, user))
            return "cf-strike-action-needs-ignored-taunt";

        if (action.Ranged)
        {
            if (!TryHeldRanged(user, out _, out var bow))
                return "cf-strike-action-needs-ranged";

            var count = new GetAmmoCountEvent();
            RaiseLocalEvent(bow, ref count);
            if (count.Count <= 0)
                return "cf-strike-action-needs-ammo";

            var feet = (_transform.GetWorldPosition(user) - _transform.GetWorldPosition(target)).Length();
            return _interaction.InRangeUnobstructed(user, target, feet + 1f) ? null : "cf-strike-action-no-line";
        }

        if (action.TwoWeapons
            && (!_hands.TryGetActiveItem(user, out var main)
                || !HasComp<CFMeleeWeaponComponent>(main.Value)
                || !_strikes.TryOffHandWeapon(user, main.Value, out _, out _)))
        {
            return "cf-strike-action-needs-two-weapons";
        }

        if (action.TwoHandedGrip
            && (!_hands.TryGetActiveItem(user, out var held)
                || !HasComp<CFMeleeWeaponComponent>(held.Value)
                || CompOrNull<CFWeaponComponent>(held.Value)?.Hands == CFWeaponHands.Two
                || _hands.CountFreeHands(user) == 0))
        {
            return "cf-strike-action-needs-free-hand";
        }

        return null;
    }

    private void Strike(Entity<CFStrikeActionComponent> ent, EntityUid user, EntityUid target)
    {
        var action = ent.Comp;
        void Resolved(CFDegree degree, int damage) => RunEffects(ent, user, target, degree, damage);

        if (action.Ranged)
        {
            if (!TryHeldRanged(user, out var shot, out var bow))
                return;

            SpendArrow(user, bow);
            _strikes.PerformStrike(user, target, shot, bow, melee: false, attackPenalty: action.AttackBonus, resolved: Resolved);
            return;
        }

        CFStrikeWeaponStats stats;
        EntityUid weapon;
        if (action.UnarmedOnly)
        {
            stats = _strikes.UnarmedStats(user);
            weapon = user;
        }
        else if (!_strikes.TryMeleeWeapon(user, out stats, out weapon))
        {
            return;
        }

        if (action.TwoHandedGrip)
            stats = WithTwoHandedGrip(stats);

        // Spent first, so a finisher that drops its target can earn panache back.
        if (action.Finisher && TryComp(user, out CFCombatOptionsComponent? options))
        {
            options.Panache = false;
            Dirty(user, options);
        }

        var hit = _strikes.PerformStrike(user, target, stats, weapon, melee: true, attackPenalty: action.AttackBonus, extraDice: action.ExtraDice, finisher: action.Finisher, resolved: Resolved);
        if (action.TwoWeapons && _strikes.TryOffHandWeapon(user, weapon, out var off, out var offHand))
            _strikes.PerformStrike(user, target, off, offHand, melee: true, attackPenalty: action.AttackBonus, skipPrecision: hit, resolved: Resolved);

        if (action.TauntsTarget)
            _taunts.Taunt(user, target);

        if (action.Reloads)
            _reload.TryReload(user, prepaid: true);

        if (action.RechargesSpellstrike)
            _activities.RechargeSpellstrike(user);
    }

    private void RunEffects(Entity<CFStrikeActionComponent> ent, EntityUid user, EntityUid target, CFDegree degree, int damage)
    {
        if (degree is not (CFDegree.Success or CFDegree.CriticalSuccess) || ent.Comp.EffectsNeedDamage && damage <= 0)
            return;

        var effects = degree == CFDegree.CriticalSuccess && ent.Comp.OnCritical.Count > 0 ? ent.Comp.OnCritical : ent.Comp.OnHit;
        _effects.Run(effects, new CFEffectArgs
        {
            User = user,
            Source = ent,
            Target = target,
            Position = Transform(target).Coordinates,
            Degree = degree,
            Defense = CFDefenseType.ArmorClass,
        });
    }

    private bool TryHeldRanged(EntityUid user, out CFStrikeWeaponStats stats, out EntityUid weapon)
    {
        stats = default;
        weapon = default;
        if (!_hands.TryGetActiveItem(user, out var held) || !_weapons.TryStats(held.Value, out stats) || !stats.Ranged)
            return false;

        weapon = held.Value;
        return true;
    }

    private void SpendArrow(EntityUid user, EntityUid bow)
    {
        var ammo = new List<(EntityUid? Entity, IShootable Shootable)>();
        RaiseLocalEvent(bow, new TakeAmmoEvent(1, ammo, Transform(user).Coordinates, user));
        foreach (var (arrow, _) in ammo)
        {
            if (arrow is { } entity)
                Del(entity);
        }
    }
}
