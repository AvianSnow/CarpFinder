using Content.Shared.CF.DamageTypes;
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
using Content.Shared.CF.Traits;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
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
    private static readonly TimeSpan MarkLasts = TimeSpan.FromSeconds(6);

    /// <summary>Far enough to find any champion whose 15 foot aura could hold the victim. The aura itself has the last word.</summary>
    private const float AllySearchRange = 5f;

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
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFAuraSystem _auras = default!;
    [Dependency] private CFImplementSystem _implements = default!;
    [Dependency] private CFTauntSystem _taunts = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFManeuverSystem _maneuvers = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
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
            if (known.Reaction.Trigger == CFReactionTrigger.EnemyLeftReach
                && (!known.Reaction.PreyOnly || options.Prey == mover)
                && CanFire(watcher, known))
            {
                return true;
            }
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
        if (TryComp(mover, out CFCombatOptionsComponent? options)
            && (options.FreeStep || options.FreeStepWithShield && _shields.TryGetRaisedShield(mover, out _)))
        {
            return true;
        }

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
            if (known.Reaction.Trigger != CFReactionTrigger.EnemyLeftReach
                || !known.Reaction.Strikes
                || known.Reaction.PreyOnly && CompOrNull<CFCombatOptionsComponent>(watcher)?.Prey != mover
                || known.Reaction.ExploitOnly && CompOrNull<CFCombatOptionsComponent>(watcher)?.Exploited != mover
                || !TrySpend(watcher, known))
            {
                continue;
            }

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

    /// <summary>
    /// Damage is about to land on <paramref name="target"/>. Its own reaction goes first, then those of champions whose aura
    /// holds both it and the attacker. Returns the damage that is left. Damage only ever reaches here on the server.
    /// </summary>
    public int AnswerDamage(EntityUid target, EntityUid attacker, int amount, string damageType)
    {
        if (amount <= 0 || !_net.IsServer || target == attacker || !_mobState.IsAlive(attacker))
            return amount;

        amount = AnswerExploited(target, attacker, amount);

        if (TryComp(target, out CFReactionsComponent? own) && _factions.CanHarm(target, attacker) && _auras.Contains(target, attacker))
        {
            foreach (var known in own.Known)
            {
                if (known.Reaction.Trigger != CFReactionTrigger.WouldBeDamaged || !TrySpend(target, known))
                    continue;

                amount = Protect(target, target, attacker, known.Reaction, amount);
                break;
            }
        }

        foreach (var (uid, reactions) in _lookup.GetEntitiesInRange<CFReactionsComponent>(Transform(target).Coordinates, AllySearchRange))
        {
            if (uid == target || uid == attacker || _factions.CanHarm(uid, target) || !_factions.CanHarm(uid, attacker))
                continue;

            foreach (var known in reactions.Known)
            {
                if (known.Reaction.Trigger == CFReactionTrigger.InterceptsDamage)
                {
                    if (!CanIntercept(uid, target, attacker, known.Reaction, damageType) || !TrySpend(uid, known))
                        continue;

                    _effects.DealDamage(attacker, uid, amount, damageType, critical: false);
                    return 0;
                }

                if (known.Reaction.Trigger != CFReactionTrigger.AllyWouldBeDamaged
                    || !CanFire(uid, known)
                    || !_auras.Contains(uid, target)
                    || !_auras.Contains(uid, attacker)
                    || !TrySpend(uid, known))
                {
                    continue;
                }

                return Protect(uid, target, attacker, known.Reaction, amount);
            }
        }

        return amount;
    }

    /// <summary>An enemy marked with Exploit Vulnerability is about to hurt its marker, or an ally beside them.</summary>
    private int AnswerExploited(EntityUid target, EntityUid attacker, int amount)
    {
        foreach (var (uid, reactions) in _lookup.GetEntitiesInRange<CFReactionsComponent>(Transform(target).Coordinates, AllySearchRange))
        {
            if (CompOrNull<CFCombatOptionsComponent>(uid)?.Exploited != attacker || uid != target && _factions.CanHarm(uid, target))
                continue;

            foreach (var known in reactions.Known)
            {
                if (known.Reaction.Trigger != CFReactionTrigger.ExploitedWouldDamage
                    || !CanFire(uid, known)
                    || uid != target && (_transform.GetWorldPosition(uid) - _transform.GetWorldPosition(target)).Length() > known.Reaction.RangeFeet / CFMelee.FeetPerTile
                    || !TrySpend(uid, known))
                {
                    continue;
                }

                return Protect(uid, target, attacker, known.Reaction, amount);
            }
        }

        return amount;
    }

    private bool CanIntercept(EntityUid owner, EntityUid victim, EntityUid attacker, CFReaction reaction, string damageType)
    {
        if (reaction.DamageTypes.Count > 0 && !reaction.DamageTypes.Exists(type => CFDamageTypes.Same(_prototypes, type, damageType)))
            return false;

        var feet = _taunts.IsTauntedBy(attacker, owner) ? reaction.TauntedRangeFeet : reaction.RangeFeet;
        var distance = (_transform.GetWorldPosition(owner) - _transform.GetWorldPosition(victim)).Length();
        return distance <= feet / CFMelee.FeetPerTile;
    }

    private int Protect(EntityUid owner, EntityUid victim, EntityUid attacker, CFReaction reaction, int amount)
    {
        var level = CompOrNull<CFCombatantComponent>(owner)?.Level ?? 1;
        var resist = reaction.ResistFlat + (reaction.ResistLevel ? level : 0) + (reaction.ResistHalfLevel ? level / 2 : 0);
        amount = Math.Max(0, amount - resist);

        if (reaction.TakesExtraDie > 0)
        {
            var dice = 1 + (level - 1) / 4;
            for (var i = 0; i < dice; i++)
            {
                amount += _random.Next(1, reaction.TakesExtraDie + 1);
            }
        }

        if (reaction.MarkDamage > 0 && TryComp(owner, out CFCombatOptionsComponent? options))
        {
            options.MarkedTarget = attacker;
            options.MarkedDamage = reaction.MarkDamage;
            options.MarkedUntil = _timing.CurTime + MarkLasts;
        }

        if (reaction.FreesAlly && TryComp(victim, out CFConditionComponent? condition) && condition.Grabbed)
            _maneuvers.TryEscape((victim, condition), free: true);

        if (reaction.OnEnemy.Count > 0)
        {
            _effects.Run(reaction.OnEnemy, new CFEffectArgs
            {
                User = owner,
                Source = owner,
                Target = attacker,
                Position = Transform(attacker).Coordinates,
                Degree = CFDegree.Success,
                Defense = CFDefenseType.None,
            });
        }

        if (reaction.Strikes && CFFlanking.InReach(_transform.GetWorldPosition(owner), _transform.GetWorldPosition(attacker), CFFlanking.DefaultReachFeet))
            _strikes.ReactionStrike(owner, attacker);

        return amount;
    }

    /// <summary>Lets a creature answer an enemy's critical miss, as Opportune Riposte does.</summary>
    public void AnswerCriticalMiss(EntityUid target, EntityUid attacker)
    {
        if (!TryComp(target, out CFReactionsComponent? reactions)
            || !_mobState.IsAlive(target)
            || !_mobState.IsAlive(attacker)
            || !_factions.CanHarm(target, attacker)
            || !CFFlanking.InReach(_transform.GetWorldPosition(target), _transform.GetWorldPosition(attacker), CFFlanking.DefaultReachFeet))
        {
            return;
        }

        foreach (var known in reactions.Known)
        {
            if (known.Reaction.Trigger != CFReactionTrigger.AttackCriticallyMissed
                || !known.Reaction.Strikes
                || !CanFire(target, known)
                || !TrySpend(target, known))
            {
                continue;
            }

            _strikes.ReactionStrike(target, attacker);
            return;
        }
    }

    /// <summary>Gives a saving throw the chance of being improved. Fires only if that could help.</summary>
    public CFDegree AnswerSave(
        EntityUid uid,
        CFDefenseType defense,
        int d20,
        int modifier,
        int dc,
        ref int total,
        CFDegree degree,
        HashSet<ProtoId<CFTraitPrototype>>? traits = null)
    {
        if (defense is not (CFDefenseType.Fortitude or CFDefenseType.Reflex or CFDefenseType.Will)
            || !TryComp(uid, out CFReactionsComponent? reactions))
        {
            return degree;
        }

        var existing = Math.Max(0, _bonuses.TotalOfType(uid, CFBonusStat.Saves, CFBonusType.Circumstance));
        foreach (var known in reactions.Known)
        {
            var reaction = known.Reaction;
            if (reaction.Trigger != CFReactionTrigger.SaveWouldFail
                || reaction.Save is { } only && only != defense
                || reaction.Trait is { } trait && traits?.Contains(trait) != true
                || !CanFire(uid, known))
            {
                continue;
            }

            if (reaction.Rerolls)
            {
                if (degree.IsSuccess() || !TrySpend(uid, known))
                    continue;

                var second = _random.Next(1, 21);
                var rerolled = CFDegrees.DegreeOfSuccess(second, modifier, dc);
                if (reaction.KeepsBetter && rerolled <= degree)
                    return degree;

                total = second + modifier;
                return rerolled;
            }

            var gain = reaction.SaveBonus - existing;
            if (gain <= 0)
                continue;

            var result = CFDegrees.DegreeOfSuccess(d20, modifier + gain, dc);
            if (result <= degree || !TrySpend(uid, known))
                continue;

            total += gain;
            if (reaction.GainsPanache && result.IsSuccess())
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
               && (known.Reaction.Implement is not { } kind || _implements.IsHolding(uid, kind))
               && _mobState.IsAlive(uid)
               && TryComp(uid, out CFCombatOptionsComponent? options)
               && (known.Reaction.Free || options.ReactionSpent == 0);
    }

    private bool TrySpend(EntityUid uid, CFKnownReaction known)
    {
        if (!CanFire(uid, known) || !TryComp(uid, out CFCombatOptionsComponent? options))
            return false;

        if (!known.Reaction.Free)
        {
            options.ReactionSpent = CFReactions.Granted;
            Dirty(uid, options);
        }

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
