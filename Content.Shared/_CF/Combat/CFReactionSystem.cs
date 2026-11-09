using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Items;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Runs the reactions granted by <c>CFGrantReaction</c>. A creature has one reaction until its action pool is next full,
/// shared with the older hard-coded reactions in <see cref="CFReactions"/>.
/// </summary>
public sealed partial class CFReactionSystem : EntitySystem
{
    /// <summary>One Step gets a creature clear of everyone it is leaving at about the same moment.</summary>
    private static readonly TimeSpan StepCovers = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
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
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<CFLeftReachComponent>();
        while (query.MoveNext(out var uid, out var left))
        {
            foreach (var mover in left.Left)
            {
                AnswerLeftReach(uid, mover);
            }

            RemCompDeferred(uid, left);
        }
    }

    /// <summary>
    /// Called when <paramref name="mover"/> leaves the reach circle of <paramref name="watcher"/>. An enemy that could answer
    /// gets to, unless the mover walked out and could pay for a Step. Being thrown or dragged out does not count, and neither
    /// does the watcher being the one who walked off.
    /// </summary>
    public void NoteLeftReach(EntityUid watcher, EntityUid mover)
    {
        if (!_net.IsServer || !CanAnswerLeftReach(watcher, mover) || !LeftByItself(watcher, mover))
            return;

        if (TryComp(mover, out InputMoverComponent? input) && !input.Sprinting && TryPayForStep(mover))
            return;

        EnsureComp<CFLeftReachComponent>(watcher).Left.Add(mover);
    }

    private bool CanAnswerLeftReach(EntityUid watcher, EntityUid mover)
    {
        if (!_mobState.IsAlive(watcher)
            || !_factions.CanHarm(watcher, mover)
            || _stealth.IsUnseenBy(mover, watcher)
            || !TryComp(watcher, out CFCombatOptionsComponent? options)
            || options.ReactionSpent != 0)
        {
            return false;
        }

        if (options.ReactiveStrike)
            return true;

        if (!TryComp(watcher, out CFReactionsComponent? reactions))
            return false;

        foreach (var known in reactions.Known)
        {
            if (known.Reaction.Trigger == CFReactionTrigger.EnemyLeftReach && CanFire(watcher, known))
                return true;
        }

        return false;
    }

    private bool LeftByItself(EntityUid watcher, EntityUid mover)
    {
        if (TerminatingOrDeleted(mover)
            || !_mobState.IsAlive(mover)
            || HasComp<ThrownItemComponent>(mover)
            || TryComp(mover, out PullableComponent? pullable) && pullable.BeingPulled)
        {
            return false;
        }

        var away = _transform.GetWorldPosition(mover) - _transform.GetWorldPosition(watcher);
        if (away.LengthSquared() <= 0f)
            return false;

        away = Vector2.Normalize(away);
        var moverSpeed = Vector2.Dot(_physics.GetMapLinearVelocity(mover), away);
        var watcherSpeed = Vector2.Dot(_physics.GetMapLinearVelocity(watcher), -away);
        return moverSpeed > 0f && moverSpeed >= watcherSpeed;
    }

    /// <summary>A Step costs 1 action. False if the mover cannot pay, and is then caught like anyone else.</summary>
    private bool TryPayForStep(EntityUid mover)
    {
        var pool = CompOrNull<CFActionPoolComponent>(mover);
        var now = _timing.CurTime;
        if (pool != null && now < pool.StepCoveredUntil)
            return true;

        if (!_actions.TrySpendActions(mover, 1, stealthy: true))
            return false;

        if (pool != null)
            pool.StepCoveredUntil = now + StepCovers;

        _popup.PopupEntity(Loc.GetString("cf-reaction-step"), mover, mover);
        return true;
    }

    private void AnswerLeftReach(EntityUid watcher, EntityUid mover)
    {
        if (TerminatingOrDeleted(mover)
            || !_mobState.IsAlive(watcher)
            || !_mobState.IsAlive(mover)
            || !_factions.CanHarm(watcher, mover))
        {
            return;
        }

        if (_conditions.TrySpendReaction(watcher, CFReactions.Strike))
        {
            _popup.PopupEntity(Loc.GetString("cf-reaction-reactive-strike"), watcher);
            _strikes.ReactionStrike(watcher, mover);
            return;
        }

        if (!TryComp(watcher, out CFReactionsComponent? reactions))
            return;

        foreach (var known in reactions.Known)
        {
            if (known.Reaction.Trigger != CFReactionTrigger.EnemyLeftReach || !known.Reaction.Strikes || !TrySpend(watcher, known))
                continue;

            _strikes.ReactionStrike(watcher, mover);
            return;
        }
    }

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
