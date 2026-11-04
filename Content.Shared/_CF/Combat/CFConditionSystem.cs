using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.Movement.Systems;
using Content.Shared.Overlays;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Applies frightened, grabbed, concealed, immobilized, and the other conditions Strikes read.
/// A full action pool is a new turn, so frightened eases and a raised shield drops then.
/// </summary>
public sealed partial class CFConditionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFEncumbranceSystem _encumbrance = default!;

    private const float TurnSeconds = 6f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFConditionComponent>();
        while (query.MoveNext(out var uid, out var cond))
        {
            var dirty = false;
            if (cond.Immobilized && now >= cond.ImmobilizedUntil)
            {
                cond.Immobilized = false;
                dirty = true;
                _speed.RefreshMovementSpeedModifiers(uid);
            }

            if (cond.Concealed && now >= cond.ConcealedUntil)
            {
                cond.Concealed = false;
                cond.FlatCheckDc = 0;
                cond.ConcealBreaks = false;
                dirty = true;
            }

            if (cond.Sanctuary && now >= cond.SanctuaryUntil)
            {
                cond.Sanctuary = false;
                cond.SanctuaryDc = 0;
                dirty = true;
            }

            if (cond.Charmed && now >= cond.CharmedUntil)
            {
                cond.Charmed = false;
                cond.Charmer = null;
                dirty = true;
            }

            if (cond.Calmed && now >= cond.CalmedUntil)
            {
                cond.Calmed = false;
                dirty = true;
            }

            if (cond.Blinded && now >= cond.BlindUntil)
            {
                cond.Blinded = false;
                dirty = true;
            }

            if (cond.OffGuardAll && now >= cond.OffGuardAllUntil)
            {
                cond.OffGuardAll = false;
                dirty = true;
            }

            if (cond.Grabbed && (cond.Grabber is not { } grabber || !Exists(grabber) || !InMelee(grabber, uid)))
            {
                cond.Grabbed = false;
                cond.Grabber = null;
                cond.Immobilized = false;
                dirty = true;
                _speed.RefreshMovementSpeedModifiers(uid);
            }

            DropExpired(cond.OffGuardUntil, cond.OffGuardTo, now);
            DropExpired(cond.DemoralizedUntil, null, now);

            if (dirty)
                Dirty(uid, cond);
        }
    }

    [SubscribeLocalEvent]
    private void OnMove(Entity<CFConditionComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Immobilized || ent.Comp.Grabbed)
            args.ModifySpeed(0f);
    }

    [SubscribeLocalEvent]
    private void OnTurn(EntityUid uid, CFActionPoolComponent component, CFTurnRefilledEvent args)
    {
        if (!_net.IsServer)
            return;

        if (TryComp(uid, out CFConditionComponent? cond))
        {
            if (cond.Frightened > 0)
            {
                cond.Frightened--;
                Dirty(uid, cond);
            }

            if (cond.RaisedShield)
            {
                cond.RaisedShield = false;
                Dirty(uid, cond);
            }
        }

        if (!TryComp(uid, out CFCombatOptionsComponent? opt))
            return;

        opt.ReactionSpent = 0;
        opt.PreyHitThisTurn = false;
        Dirty(uid, opt);
    }

    public CFConditionComponent Ensure(EntityUid uid)
    {
        return EnsureComp<CFConditionComponent>(uid);
    }

    public int Frightened(EntityUid uid)
    {
        return TryComp(uid, out CFConditionComponent? cond) ? cond.Frightened : 0;
    }

    public void ApplyFrightened(EntityUid target, int value)
    {
        if (value <= 0)
            return;

        var cond = Ensure(target);
        cond.Frightened = Math.Max(cond.Frightened, Math.Clamp(value, 1, 4));
        Dirty(target, cond);
        _popup.PopupEntity(Loc.GetString("cf-cond-frightened", ("value", cond.Frightened)), target);
    }

    public void ApplyGrabbed(EntityUid target, EntityUid grabber)
    {
        var cond = Ensure(target);
        cond.Grabbed = true;
        cond.Grabber = grabber;
        cond.Immobilized = true;
        cond.ImmobilizedUntil = _timing.CurTime + TimeSpan.FromSeconds(60);
        Dirty(target, cond);
        _speed.RefreshMovementSpeedModifiers(target);
        _popup.PopupEntity(Loc.GetString("cf-cond-grabbed"), target);
    }

    public void ReleaseGrab(EntityUid target)
    {
        if (!TryComp(target, out CFConditionComponent? cond) || !cond.Grabbed)
            return;

        cond.Grabbed = false;
        cond.Grabber = null;
        cond.Immobilized = false;
        Dirty(target, cond);
        _speed.RefreshMovementSpeedModifiers(target);
    }

    public void ApplyImmobilized(EntityUid target, float seconds)
    {
        var cond = Ensure(target);
        cond.Immobilized = true;
        cond.ImmobilizedUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
        _speed.RefreshMovementSpeedModifiers(target);
        _popup.PopupEntity(Loc.GetString("cf-cond-immobilized"), target);
    }

    public void ApplyConcealed(EntityUid target, int flatDc, bool breaks, float seconds)
    {
        var cond = Ensure(target);
        cond.Concealed = true;
        cond.FlatCheckDc = Math.Max(cond.FlatCheckDc, flatDc);
        cond.ConcealBreaks = breaks;
        cond.ConcealedUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
        _popup.PopupEntity(Loc.GetString(breaks ? "cf-cond-hidden" : "cf-cond-concealed"), target);
    }

    public void ApplySanctuary(EntityUid target, int dc, float seconds)
    {
        var cond = Ensure(target);
        cond.Sanctuary = true;
        cond.SanctuaryDc = dc;
        cond.SanctuaryUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
        _popup.PopupEntity(Loc.GetString("cf-cond-sanctuary"), target);
    }

    public void ApplyCharm(EntityUid target, EntityUid caster, float seconds)
    {
        var cond = Ensure(target);
        cond.Charmed = true;
        cond.Charmer = caster;
        cond.CharmedUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
        _popup.PopupEntity(Loc.GetString("cf-cond-charmed"), target);
    }

    public void ApplyCalm(EntityUid target, float seconds)
    {
        var cond = Ensure(target);
        cond.Calmed = true;
        cond.CalmedUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
        _popup.PopupEntity(Loc.GetString("cf-cond-calmed"), target);
    }

    public void ApplyBlind(EntityUid target, float seconds)
    {
        var cond = Ensure(target);
        cond.Blinded = true;
        cond.BlindUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
        _popup.PopupEntity(Loc.GetString("cf-cond-blinded"), target);
    }

    public void ApplyDarkvision(EntityUid target)
    {
        var vision = EnsureComp<NightVisionComponent>(target);
        vision.Enabled = true;
        vision.Prioritized = true;
        vision.LightingColor = new Color((byte) 0x9B, (byte) 0xB6, (byte) 0xFF, (byte) 0x30);
        vision.NoiseAmount = 0f;
        Dirty(target, vision);
        _popup.PopupEntity(Loc.GetString("cf-cond-darkvision"), target);
    }

    public void SetOffGuardAll(EntityUid target, float seconds)
    {
        var cond = Ensure(target);
        cond.OffGuardAll = true;
        cond.OffGuardAllUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
    }

    public void SetOffGuardTo(EntityUid target, EntityUid attacker, float seconds)
    {
        var cond = Ensure(target);
        cond.OffGuardTo.Add(attacker);
        cond.OffGuardUntil[attacker] = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
    }

    public bool IsOffGuardTo(EntityUid target, EntityUid attacker)
    {
        if (!TryComp(target, out CFConditionComponent? cond))
            return false;

        return cond.Grabbed || cond.OffGuardAll || cond.Blinded || cond.OffGuardTo.Contains(attacker);
    }

    public bool IsCalmed(EntityUid uid)
    {
        return TryComp(uid, out CFConditionComponent? cond) && cond.Calmed;
    }

    public bool IsCharmedAgainst(EntityUid attacker, EntityUid target)
    {
        if (TryComp(attacker, out CFConditionComponent? attackerCond)
            && attackerCond.Charmed
            && attackerCond.Charmer == target)
            return true;

        return TryComp(target, out CFConditionComponent? targetCond)
               && targetCond.Charmed
               && targetCond.Charmer == attacker;
    }

    /// <summary>
    /// A concealed or hidden creature, or a blinded attacker, forces one flat check.
    /// Failure means the effect misses. Attacking yourself skips it.
    /// </summary>
    public bool PassFlatCheck(EntityUid attacker, EntityUid target)
    {
        if (attacker == target)
            return true;

        var dc = 0;
        if (TryComp(attacker, out CFConditionComponent? blind) && blind.Blinded)
            dc = 11;

        if (TryComp(target, out CFConditionComponent? cond) && cond.Concealed)
            dc = Math.Max(dc, cond.FlatCheckDc);

        if (dc <= 0)
            return true;

        var roll = _random.Next(1, 21);
        if (roll >= dc)
            return true;

        _popup.PopupEntity(Loc.GetString("cf-cond-flat-miss"), attacker, attacker);
        return false;
    }

    public void NoteAttack(EntityUid actor)
    {
        if (!TryComp(actor, out CFConditionComponent? cond) || !cond.ConcealBreaks)
            return;

        cond.Concealed = false;
        cond.FlatCheckDc = 0;
        cond.ConcealBreaks = false;
        Dirty(actor, cond);
    }

    public bool TrySpendReaction(EntityUid uid, int bit)
    {
        if (!TryComp(uid, out CFCombatOptionsComponent? opt))
            return false;

        if (opt.ReactionSpent != 0 || !CFReactions.Knows(opt, bit))
            return false;

        opt.ReactionSpent = bit;
        Dirty(uid, opt);
        return true;
    }

    public void RaiseShield(EntityUid uid)
    {
        var cond = Ensure(uid);
        cond.RaisedShield = true;
        Dirty(uid, cond);
        _popup.PopupEntity(Loc.GetString("cf-act-shield-raised"), uid, uid);
    }

    /// <summary>
    /// Uses a shield spell's hardness once, then the spell's block is gone.
    /// Returns the damage that remains.
    /// </summary>
    public int BlockWithSpell(EntityUid target, int damage)
    {
        if (damage <= 0 || !TryComp(target, out CFConditionComponent? cond) || cond.ShieldHardness <= 0)
            return damage;

        var blocked = Math.Min(damage, cond.ShieldHardness);
        damage -= blocked;
        cond.ShieldHardness = 0;
        Dirty(target, cond);
        _popup.PopupEntity(Loc.GetString("cf-act-shield-block", ("amount", blocked)), target);
        return damage;
    }

    public void GrantSpellShield(EntityUid target, int hardness)
    {
        var cond = Ensure(target);
        cond.ShieldHardness = Math.Max(cond.ShieldHardness, hardness);
        Dirty(target, cond);
    }

    public bool TryDemoralize(EntityUid user, EntityUid target, int circumstance = 0)
    {
        if (!TryComp(user, out CFCombatantComponent? combat) || !TryComp(user, out CFAbilityScoreComponent? abilities))
            return false;

        var cond = Ensure(target);
        if (cond.DemoralizedUntil.TryGetValue(user, out var until) && until > _timing.CurTime)
        {
            _popup.PopupEntity(Loc.GetString("cf-act-demoralize-immune"), user, user);
            return false;
        }

        cond.DemoralizedUntil[user] = _timing.CurTime + TimeSpan.FromSeconds(60);
        var skill = CFDefense.SkillModifier(
            _prototypes,
            combat,
            abilities,
            "Intimidation",
            CFAbility.Charisma,
            circumstance,
            Frightened(user));
        var dc = 10 + CFDefense.SaveModifier(
            _prototypes,
            CompOrNull<CFCombatantComponent>(target),
            CompOrNull<CFAbilityScoreComponent>(target),
            CFDefenseType.Will,
            SaveStatus(target),
            cond.Frightened);
        var degree = CFDefense.Roll(_random, skill, dc);
        if (degree is CFDegree.Success or CFDegree.CriticalSuccess)
        {
            ApplyFrightened(target, degree == CFDegree.CriticalSuccess ? 2 : 1);
            if (TryComp(user, out CFCombatOptionsComponent? style) && style.PanacheStyle)
            {
                style.Panache = true;
                Dirty(user, style);
                _popup.PopupEntity(Loc.GetString("cf-act-panache"), user, user);
            }

            return true;
        }

        _popup.PopupEntity(Loc.GetString("cf-act-demoralize-fail"), user, user);
        return false;
    }

    public bool TryFeint(EntityUid user, EntityUid target)
    {
        if (!TryComp(user, out CFCombatantComponent? combat) || !TryComp(user, out CFAbilityScoreComponent? abilities))
            return false;

        var skill = CFDefense.SkillModifier(
            _prototypes,
            combat,
            abilities,
            "Deception",
            CFAbility.Charisma,
            0,
            Frightened(user));
        var dc = 10 + CFDefense.PerceptionModifier(
            _prototypes,
            CompOrNull<CFCombatantComponent>(target),
            CompOrNull<CFAbilityScoreComponent>(target),
            DefenseStatus(target, CFDefenseType.Perception),
            Frightened(target));
        var degree = CFDefense.Roll(_random, skill, dc);
        if (degree is not (CFDegree.Success or CFDegree.CriticalSuccess))
        {
            _popup.PopupEntity(Loc.GetString("cf-act-feint-fail"), user, user);
            return false;
        }

        if (degree == CFDegree.CriticalSuccess)
            SetOffGuardAll(target, TurnSeconds);
        else
            SetOffGuardTo(target, user, TurnSeconds);

        if (TryComp(user, out CFCombatOptionsComponent? style) && style.PanacheStyle)
        {
            style.Panache = true;
            Dirty(user, style);
            _popup.PopupEntity(Loc.GetString("cf-act-panache"), user, user);
        }

        _popup.PopupEntity(Loc.GetString("cf-act-feint"), target);
        return true;
    }

    public bool TrySkill(EntityUid user, EntityUid target, string skill, CFAbility ability, CFDefenseType defense)
    {
        return RollSkill(user, target, skill, ability, defense).IsSuccess();
    }

    /// <summary>
    /// A skill check against a target's defense DC (10 + their save or Perception).
    /// Worn armor's check penalty applies unless <paramref name="attackTrait"/> (Trip, Shove, Grapple, Disarm).
    /// <paramref name="itemBonus"/> is a weapon's potency when it has the matching maneuver trait.
    /// </summary>
    public CFDegree RollSkill(
        EntityUid user,
        EntityUid target,
        string skill,
        CFAbility ability,
        CFDefenseType defense,
        bool attackTrait = false,
        int itemBonus = 0)
    {
        if (!TryComp(user, out CFCombatantComponent? combat) || !TryComp(user, out CFAbilityScoreComponent? abilities))
            return CFDegree.Failure;

        var mod = CFDefense.SkillModifier(_prototypes, combat, abilities, skill, ability, itemBonus, Frightened(user));
        mod += _armor.CheckPenalty(user, skill, ability, attackTrait);
        if (ability == CFAbility.Dexterity)
            mod -= Clumsy(user);

        var dc = 10 + CFDefense.SaveModifier(
            _prototypes,
            CompOrNull<CFCombatantComponent>(target),
            CompOrNull<CFAbilityScoreComponent>(target),
            defense,
            DefenseStatus(target, defense),
            Frightened(target));
        return CFDefense.Roll(_random, mod, dc);
    }

    /// <summary>
    /// A skill check against a fixed DC (disabling a trap, picking a lock). Armor check penalty and clumsy apply.
    /// <paramref name="total"/> is the d20 plus modifier, for the popup.
    /// </summary>
    public CFDegree RollSkillAgainst(
        EntityUid user,
        string skill,
        CFAbility ability,
        int dc,
        out int total,
        bool attackTrait = false,
        int itemBonus = 0)
    {
        var mod = 0;
        if (TryComp(user, out CFCombatantComponent? combat) && TryComp(user, out CFAbilityScoreComponent? abilities))
            mod = CFDefense.SkillModifier(_prototypes, combat, abilities, skill, ability, itemBonus, Frightened(user));
        else
            mod -= Frightened(user);

        mod += _armor.CheckPenalty(user, skill, ability, attackTrait);
        if (ability == CFAbility.Dexterity)
            mod -= Clumsy(user);

        var d20 = _random.Next(1, 21);
        total = d20 + mod;
        return CFDegrees.DegreeOfSuccess(d20, mod, dc);
    }

    /// <summary>
    /// Clumsy value: a penalty to AC, Reflex saves, ranged attack rolls, and Dexterity-based checks.
    /// Encumbered creatures are clumsy 1.
    /// </summary>
    public int Clumsy(EntityUid uid)
    {
        return _encumbrance.IsEncumbered(uid) ? 1 : 0;
    }

    public void KnockProne(EntityUid target)
    {
        _stun.TryKnockdown(target, TimeSpan.FromSeconds(3), refresh: true, autoStand: true, drop: false, force: true);
        _popup.PopupEntity(Loc.GetString("cf-spell-prone"), target);
    }

    /// <summary>Shoves the target away from <paramref name="origin"/>, or toward it when <paramref name="pull"/>.</summary>
    public void Push(EntityUid origin, EntityUid target, int feet, bool pull = false)
    {
        if (!TryComp(target, out PhysicsComponent? body) || body.BodyType == BodyType.Static)
            return;

        var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(origin);
        if (direction.LengthSquared() < 0.01f)
            direction = new Vector2(1f, 0f);
        else
            direction = Vector2.Normalize(direction);

        if (pull)
            direction = -direction;

        var speed = Math.Max(1, feet) / CFMelee.FeetPerTile * 6f;
        _physics.SetLinearVelocity(target, direction * speed, body: body);
    }

    /// <summary>
    /// The one entry point effects use to give a condition. <paramref name="dc"/> is the source's DC
    /// (sanctuary's Will save). <paramref name="source"/> is the creature that caused it (charm).
    /// </summary>
    public void ApplyCondition(EntityUid target, CFConditionKind kind, int value, float seconds, EntityUid source, int dc)
    {
        seconds = Math.Max(1f, seconds);
        switch (kind)
        {
            case CFConditionKind.Frightened:
                ApplyFrightened(target, value);
                break;
            case CFConditionKind.Prone:
                KnockProne(target);
                break;
            case CFConditionKind.Asleep:
                KnockOutFor(target, seconds, "cf-spell-sleep");
                break;
            case CFConditionKind.Paralyzed:
                KnockOutFor(target, seconds, "cf-spell-paralyze");
                break;
            case CFConditionKind.Immobilized:
                ApplyImmobilized(target, seconds);
                break;
            case CFConditionKind.Concealed:
                ApplyConcealed(target, 5, breaks: false, seconds);
                break;
            case CFConditionKind.Invisible:
                ApplyConcealed(target, 11, breaks: true, seconds);
                break;
            case CFConditionKind.Sanctuary:
                ApplySanctuary(target, dc, seconds);
                break;
            case CFConditionKind.Charmed:
                ApplyCharm(target, source, seconds);
                break;
            case CFConditionKind.Calmed:
                ApplyCalm(target, seconds);
                break;
            case CFConditionKind.Blinded:
                ApplyBlind(target, seconds);
                break;
            case CFConditionKind.Darkvision:
                ApplyDarkvision(target);
                break;
            case CFConditionKind.SpellShield:
                GrantSpellShield(target, value);
                break;
            case CFConditionKind.OffGuard:
                SetOffGuardAll(target, seconds);
                break;
        }
    }

    private void KnockOutFor(EntityUid target, float seconds, string message)
    {
        _stun.TryKnockdown(
            target,
            TimeSpan.FromSeconds(seconds),
            refresh: true,
            autoStand: false,
            drop: true,
            force: true);
        _popup.PopupEntity(Loc.GetString(message), target);
    }

    private int SaveStatus(EntityUid uid)
    {
        return _bonuses.Total(uid, CFBonusStat.Saves);
    }

    private int DefenseStatus(EntityUid uid, CFDefenseType defense)
    {
        var status = defense == CFDefenseType.Perception
            ? _bonuses.Total(uid, CFBonusStat.Perception)
            : SaveStatus(uid);

        return defense == CFDefenseType.Reflex ? status - Clumsy(uid) : status;
    }

    private static void DropExpired(Dictionary<EntityUid, TimeSpan> times, HashSet<EntityUid>? also, TimeSpan now)
    {
        List<EntityUid>? expired = null;
        foreach (var (who, until) in times)
        {
            if (now < until)
                continue;

            expired ??= new List<EntityUid>();
            expired.Add(who);
        }

        if (expired == null)
            return;

        foreach (var who in expired)
        {
            times.Remove(who);
            also?.Remove(who);
        }
    }

    private bool InMelee(EntityUid from, EntityUid to)
    {
        return CFFlanking.InReach(_transform.GetWorldPosition(from), _transform.GetWorldPosition(to), CFFlanking.DefaultReachFeet);
    }
}
