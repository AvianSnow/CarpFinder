using Content.Shared.CF.Checks;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Feats that live on the normal attack inputs instead of the wheel. Riders ride on a plain Strike.
/// A wide swing at one creature becomes Double Slice, Twin Feint, or Vicious Swing.
/// </summary>
public sealed partial class CFStrikeSystem
{
    /// <summary>The feat these take is printed as two actions. The swing itself already pays one.</summary>
    private const int FeatSwingActions = 2;

    /// <summary>Off-guard is automatic against Twin Feint's second Strike, so it only has to outlast that Strike.</summary>
    private const float TwinFeintOffGuardSeconds = 1f;

    /// <summary>Snagging Strike lasts until the striker's next turn, which is 6 seconds in real time.</summary>
    private const float SnaggingOffGuardSeconds = 6f;

    /// <summary>
    /// Takes over a wide swing that reached one creature. Two or more creatures stay a normal wide swing.
    /// Returns false when no feat applies, or the creature cannot pay for it.
    /// </summary>
    private bool TryFeatSwing(MeleeHitEvent args, CFStrikeWeaponStats stats, EntityUid weaponUid)
    {
        // Stock gives a click no direction.
        if (args.Direction == null || !TrySoleCreature(args.User, args.HitEntities, out var target))
            return false;

        return RunFeatSwing(args.User, target, stats, weaponUid, FeatSwingActions - 1);
    }

    /// <summary>The same feats for code with no swing behind it, which pays for all of it.</summary>
    public bool TryFeatStrike(EntityUid user, EntityUid target)
    {
        return _net.IsServer
               && TryMeleeWeapon(user, out var stats, out var weaponUid)
               && RunFeatSwing(user, target, stats, weaponUid, FeatSwingActions);
    }

    private bool RunFeatSwing(EntityUid user, EntityUid target, CFStrikeWeaponStats stats, EntityUid weaponUid, int actionsToPay)
    {
        if (!TryComp(user, out CFCombatOptionsComponent? style) || !_actions.CanAffordActions(user, FeatSwingActions))
            return false;

        if ((style.DoubleSlice || style.TwinFeint) && TryOffHandWeapon(user, weaponUid, out var offStats, out var offUid))
        {
            _actions.TrySpendActions(user, actionsToPay);
            if (style.DoubleSlice)
                DoubleSlice(user, target, stats, weaponUid, offStats, offUid);
            else
                TwinFeint(user, target, stats, weaponUid, offStats, offUid);

            return true;
        }

        if (!style.ViciousSwing || !_actions.CanFlourish(user))
            return false;

        _actions.TrySpendActions(user, actionsToPay);
        _actions.MarkFlourish(user);
        ViciousSwing(user, target, stats, weaponUid);
        return true;
    }

    /// <summary>The only creature the swing can harm. Objects in the arc do not count, so a wall changes nothing.</summary>
    private bool TrySoleCreature(EntityUid user, IReadOnlySet<EntityUid> hit, out EntityUid target)
    {
        target = default;
        var found = false;
        foreach (var uid in hit)
        {
            if (!HasComp<MobStateComponent>(uid) || !_factions.CanHarm(user, uid))
                continue;

            if (found)
                return false;

            target = uid;
            found = true;
        }

        return found;
    }

    /// <summary>A melee weapon in another hand. A shield counts, because every shield can bash.</summary>
    public bool TryOffHandWeapon(EntityUid user, EntityUid main, out CFStrikeWeaponStats stats, out EntityUid offHand)
    {
        stats = default!;
        offHand = default;
        if (!_hands.IsHolding(user, main))
            return false;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held == main || !HasComp<CFMeleeWeaponComponent>(held) || !_weapons.TryStats(held, out stats))
                continue;

            offHand = held;
            return true;
        }

        return false;
    }

    /// <summary>
    /// One Strike with each weapon, both at the current multiple attack penalty. The second takes −2 unless agile.
    /// It counts as two attacks. Precision damage applies once, to the first hit.
    /// Resistances still apply to each Strike, not to the combined damage.
    /// </summary>
    private void DoubleSlice(
        EntityUid user,
        EntityUid target,
        CFStrikeWeaponStats main,
        EntityUid mainUid,
        CFStrikeWeaponStats off,
        EntityUid offUid)
    {
        var mainMap = _actions.MapPenalty(user, main.Agile);
        var offMap = _actions.MapPenalty(user, off.Agile);
        var firstHit = PerformStrike(user, target, main, mainUid, melee: true, fixedMap: mainMap);
        PerformStrike(
            user,
            target,
            off,
            offUid,
            melee: true,
            attackPenalty: off.Agile ? 0 : -2,
            fixedMap: offMap,
            skipPrecision: firstHit);
    }

    /// <summary>One Strike with each weapon, with the usual penalty. The target is off-guard against the second.</summary>
    private void TwinFeint(
        EntityUid user,
        EntityUid target,
        CFStrikeWeaponStats main,
        EntityUid mainUid,
        CFStrikeWeaponStats off,
        EntityUid offUid)
    {
        PerformStrike(user, target, main, mainUid, melee: true);
        _conditions.SetOffGuardTo(target, user, TwinFeintOffGuardSeconds);
        PerformStrike(user, target, off, offUid, melee: true);
    }

    /// <summary>One extra weapon die, two at 10th level and three at 18th. It counts as two attacks.</summary>
    private void ViciousSwing(EntityUid user, EntityUid target, CFStrikeWeaponStats stats, EntityUid weaponUid)
    {
        var level = CompOrNull<CFCombatantComponent>(user)?.Level ?? 1;
        var dice = level >= 18 ? 3 : level >= 10 ? 2 : 1;
        PerformStrike(user, target, stats, weaponUid, melee: true, extraDice: dice);
        _actions.CountAttack(user);
    }

    /// <summary>What Snagging Strike and Exacting Strike add to a plain Strike.</summary>
    private void ApplyRiders(
        EntityUid user,
        EntityUid target,
        CFCombatOptionsComponent? style,
        CFDegree degree,
        int mapPenalty,
        EntityUid weaponUid,
        bool isObject,
        bool exacting)
    {
        if (style == null)
            return;

        // A critical failure still counts.
        if (style.ExactingStrike && exacting && mapPenalty != 0 && degree == CFDegree.Failure)
            _actions.UndoAttack(user);

        if (!style.SnaggingStrike || isObject || degree is not (CFDegree.Success or CFDegree.CriticalSuccess))
            return;

        // A fist uses one hand, so the free hand has to be the other one.
        var neededHands = _hands.IsHolding(user, weaponUid) ? 1 : 2;
        if (_hands.CountFreeHands(user) < neededHands)
            return;

        if (CFFlanking.InReach(_transform.GetWorldPosition(user), _transform.GetWorldPosition(target), CFFlanking.DefaultReachFeet))
            _conditions.SetOffGuardAll(target, SnaggingOffGuardSeconds);
    }
}
