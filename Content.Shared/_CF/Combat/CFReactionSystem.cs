using Content.Shared.CF.Checks;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Runs the reactions granted by <c>CFGrantReaction</c>. A creature has one reaction until its action pool is next full,
/// shared with the older hard-coded reactions in <see cref="CFReactions"/>.
/// </summary>
public sealed partial class CFReactionSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private CFStealthSystem _stealth = default!;
    [Dependency] private CFEncumbranceSystem _encumbrance = default!;
    [Dependency] private CFFlankingSystem _flanking = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFDyingSystem _dying = default!;

    public void Add(EntityUid uid, CFReaction reaction)
    {
        EnsureComp<CFReactionsComponent>(uid).Known.Add(new CFKnownReaction { Reaction = reaction });
    }

    /// <summary>A long rest gives back every daily use.</summary>
    public void RestoreDaily(EntityUid uid)
    {
        if (!TryComp(uid, out CFReactionsComponent? reactions))
            return;

        foreach (var known in reactions.Known)
        {
            known.UsedToday = 0;
        }
    }

    /// <summary>
    /// Gives a Strike that is about to land the chance of being answered. Picks the reaction that helps most, and fires
    /// none if no bonus would change the result.
    /// </summary>
    public CFDegree AnswerAttack(
        EntityUid target,
        EntityUid attacker,
        bool ranged,
        int d20,
        int attackBonus,
        ref int armorClass,
        CFDegree degree)
    {
        if (degree is not (CFDegree.Success or CFDegree.CriticalSuccess) || !TryComp(target, out CFReactionsComponent? reactions))
            return degree;

        // Circumstance bonuses do not stack, so only what is above a raised shield or the like counts.
        var existing = Math.Max(
            _shields.RaisedBonus(target),
            Math.Max(0, _bonuses.TotalOfType(target, CFBonusStat.ArmorClass, CFBonusType.Circumstance)));

        CFKnownReaction? best = null;
        var bestDegree = degree;
        var bestGain = 0;
        foreach (var known in reactions.Known)
        {
            var reaction = known.Reaction;
            var gain = reaction.AcBonus - existing;
            if (reaction.Trigger != CFReactionTrigger.AttackWouldHit
                || gain <= 0
                || !CanFire(target, known)
                || !MeetsAttackNeeds(target, attacker, ranged, reaction))
            {
                continue;
            }

            var result = CFDegrees.DegreeOfSuccess(d20, attackBonus, armorClass + gain);
            if (result < bestDegree || result == bestDegree && best != null && gain > bestGain)
            {
                best = known;
                bestDegree = result;
                bestGain = gain;
            }
        }

        if (best == null || !TrySpend(target, best))
            return degree;

        armorClass += bestGain;
        if (best.Reaction.GainsPanache && bestDegree is not (CFDegree.Success or CFDegree.CriticalSuccess))
            GainPanache(target);

        return bestDegree;
    }

    /// <summary>Gives a saving throw the chance of being improved. Fires only if the result gets better.</summary>
    public CFDegree AnswerSave(EntityUid uid, CFDefenseType defense, int d20, int modifier, int dc, ref int total, CFDegree degree)
    {
        if (defense is not (CFDefenseType.Fortitude or CFDefenseType.Reflex or CFDefenseType.Will)
            || !TryComp(uid, out CFReactionsComponent? reactions))
        {
            return degree;
        }

        var existing = Math.Max(0, _bonuses.TotalOfType(uid, CFBonusStat.Saves, CFBonusType.Circumstance));
        foreach (var known in reactions.Known)
        {
            var gain = known.Reaction.SaveBonus - existing;
            if (known.Reaction.Trigger != CFReactionTrigger.SaveWouldFail || gain <= 0 || !CanFire(uid, known))
                continue;

            var result = CFDegrees.DegreeOfSuccess(d20, modifier + gain, dc);
            if (result <= degree || !TrySpend(uid, known))
                continue;

            total += gain;
            if (known.Reaction.GainsPanache && result is CFDegree.Success or CFDegree.CriticalSuccess)
                GainPanache(uid);

            return result;
        }

        return degree;
    }

    /// <summary>True if a reaction kept the creature up at 1 Hit Point, a step further from death.</summary>
    public bool TrySurviveDrop(EntityUid uid, CFCombatantComponent combat)
    {
        if (!TryComp(uid, out CFReactionsComponent? reactions))
            return false;

        foreach (var known in reactions.Known)
        {
            if (known.Reaction.Trigger != CFReactionTrigger.WouldDrop || !CanFire(uid, known) || !TrySpend(uid, known))
                continue;

            combat.CurrentHp = 1;
            combat.Wounded++;
            Dirty(uid, combat);
            _dying.SyncHealthBar(uid, combat);
            return true;
        }

        return false;
    }

    [SubscribeLocalEvent]
    private void OnDamaged(Entity<CFReactionsComponent> ent, ref CFDamagedEvent args)
    {
        if (!_net.IsServer
            || !TryComp(ent, out CFCombatantComponent? combat)
            || combat.CurrentHp <= 0
            || !TryComp(ent, out CFCombatOptionsComponent? options))
        {
            return;
        }

        foreach (var known in ent.Comp.Known)
        {
            if (known.Reaction.Trigger != CFReactionTrigger.Damaged || !known.Reaction.EntersRage)
                continue;

            if (!options.CanRage || options.Raging || !CanFire(ent, known) || !TrySpend(ent, known))
                continue;

            options.Raging = true;
            Dirty(ent, options);
            return;
        }
    }

    private bool MeetsAttackNeeds(EntityUid target, EntityUid attacker, bool ranged, CFReaction reaction)
    {
        if (reaction.RangedOnly && !ranged)
            return false;

        if (reaction.NeedsToSeeAttacker && _stealth.IsUnseenBy(attacker, target))
            return false;

        if (reaction.NotEncumbered && _encumbrance.IsEncumbered(target))
            return false;

        if (reaction.NeedsFreeHand && _hands.CountFreeHands(target) == 0)
            return false;

        return !reaction.NotOffGuard
               || !_flanking.IsOffGuardTo(target, attacker) && !_conditions.IsOffGuardTo(target, attacker);
    }

    private bool CanFire(EntityUid uid, CFKnownReaction known)
    {
        return (known.Reaction.PerDay <= 0 || known.UsedToday < known.Reaction.PerDay)
               && _mobState.IsAlive(uid)
               && TryComp(uid, out CFCombatOptionsComponent? options)
               && options.ReactionSpent == 0;
    }

    private bool TrySpend(EntityUid uid, CFKnownReaction known)
    {
        if (!CanFire(uid, known) || !TryComp(uid, out CFCombatOptionsComponent? options))
            return false;

        options.ReactionSpent = CFReactions.Granted;
        Dirty(uid, options);
        known.UsedToday++;
        if (_net.IsServer)
            _popup.PopupEntity(Loc.GetString(known.Reaction.Name), uid);

        return true;
    }

    private void GainPanache(EntityUid uid)
    {
        if (!TryComp(uid, out CFCombatOptionsComponent? options))
            return;

        options.Panache = true;
        Dirty(uid, options);
    }
}
