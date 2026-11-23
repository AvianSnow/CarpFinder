using Content.Shared.CF.Companions;
using System.Linq;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Player;
using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Movement.Systems;
using Content.Shared.Overlays;
using Content.Shared.Popups;
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
    private const int ShovesToProne = 3;

    /// <summary>What a creature resists when someone Demoralizes it.</summary>
    private static readonly HashSet<ProtoId<CFTraitPrototype>> DemoralizeTraits = new() { "Emotion", "Fear", "Mental" };

    /// <summary>What a creature resists when someone Feints it.</summary>
    private static readonly HashSet<ProtoId<CFTraitPrototype>> FeintTraits = new() { "Mental" };

    private static readonly TimeSpan ShoveWindow = TimeSpan.FromSeconds(6);

    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private CFReactionSystem _reactions = default!;
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
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private CFEncumbranceSystem _encumbrance = default!;
    [Dependency] private CFStealthSystem _stealth = default!;
    [Dependency] private Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;

    private const float TurnSeconds = 6f;

    /// <summary>The flat check to affect a hidden creature.</summary>
    public const int HiddenFlatCheck = 11;

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

            if (cond.ClumsyValue > 0 && now >= cond.ClumsyUntil)
            {
                cond.ClumsyValue = 0;
                dirty = true;
            }

            if (cond.SickenedValue > 0 && now >= cond.SickenedUntil)
            {
                cond.SickenedValue = 0;
                dirty = true;
            }

            if (cond.StupefiedValue > 0 && now >= cond.StupefiedUntil)
            {
                cond.StupefiedValue = 0;
                dirty = true;
            }

            if (cond.ValueUntil.Count > 0)
            {
                List<CFConditionKind>? over = null;
                foreach (var (kind, until) in cond.ValueUntil)
                {
                    if (now >= until)
                        (over ??= new List<CFConditionKind>()).Add(kind);
                }

                if (over != null)
                {
                    foreach (var kind in over)
                    {
                        cond.ValueUntil.Remove(kind);
                        cond.Values.Remove(kind);
                    }

                    dirty = true;
                }
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

            if (cond.Grabbed
                && (cond.Grabber is not { } grabber
                    || !Exists(grabber)
                    || !InMelee(grabber, uid)
                    || cond.GrabbedUntil != TimeSpan.Zero && now >= cond.GrabbedUntil))
            {
                cond.Grabbed = false;
                cond.Grabber = null;
                cond.Immobilized = false;
                dirty = true;
                _speed.RefreshMovementSpeedModifiers(uid);
            }

            DropExpired(cond.OffGuardUntil, cond.OffGuardTo, now);
            DropExpired(cond.FeintedUntil, cond.FeintedOnce, now);
            DropExpired(cond.OverextendedUntil, cond.OverextendedOnce, now);
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
                ReduceFrightened(uid, CompOrNull<CFCombatOptionsComponent>(uid)?.ShakesOffFear == true ? 2 : 1);
        }

        if (!TryComp(uid, out CFCombatOptionsComponent? opt))
            return;

        opt.ReactionSpent = 0;
        opt.PreyHitThisTurn = false;
        Dirty(uid, opt);
    }

    /// <summary>Lowers frightened, but not below 1 while something is holding it there.</summary>
    public void ReduceFrightened(EntityUid uid, int amount)
    {
        if (!TryComp(uid, out CFConditionComponent? cond) || cond.Frightened <= 0)
            return;

        var floor = _timing.CurTime < cond.FrightenedFloorUntil ? 1 : 0;
        cond.Frightened = Math.Max(floor, cond.Frightened - amount);
        Dirty(uid, cond);
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
        if (CompOrNull<CFCombatOptionsComponent>(target)?.Bravery == true && --value <= 0)
            return;

        // Only a creature that is not frightened yet becomes frightened, so a reaction to that has nothing to answer otherwise.
        if (value > 0 && Frightened(target) == 0)
            value = _reactions.AnswerFrightened(target, value);

        if (value <= 0)
            return;

        var cond = Ensure(target);
        cond.Frightened = Math.Max(cond.Frightened, Math.Clamp(value, 1, 4));
        Dirty(target, cond);
        _popup.PopupEntity(Loc.GetString("cf-cond-frightened", ("value", cond.Frightened)), target);
    }

    /// <param name="seconds">How long the grab holds if it is not escaped first. 0 means until it is escaped.</param>
    public void ApplyGrabbed(EntityUid target, EntityUid grabber, float seconds = 0f)
    {
        if (_reactions.TrySlipGrab(target))
            return;

        var cond = Ensure(target);
        cond.Grabbed = true;
        cond.Grabber = grabber;
        cond.GrabbedUntil = seconds > 0f ? _timing.CurTime + TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
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

        // The grab and its pull end together.
        if (TryComp(target, out PullableComponent? pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(target, pullable);
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

    /// <param name="melee">A Feint only leaves the target off-guard against melee attacks.</param>
    public bool IsOffGuardTo(EntityUid target, EntityUid attacker, bool melee = true)
    {
        if (!TryComp(target, out CFConditionComponent? cond))
            return false;

        if (cond.Grabbed || cond.OffGuardAll || cond.Blinded || cond.OffGuardTo.Contains(attacker))
            return true;

        var now = _timing.CurTime;
        return melee
               && (now < cond.FeintedAllUntil
                   || cond.FeintedUntil.TryGetValue(attacker, out var until) && now < until);
    }

    /// <summary>
    /// <paramref name="attacker"/> has just made an attack roll against <paramref name="target"/>. A Feint good for one
    /// melee attack is used up, and so is an Overextending Feint's penalty to one attack.
    /// </summary>
    public void NoteAttackRoll(EntityUid attacker, EntityUid target, bool melee)
    {
        if (melee && TryComp(target, out CFConditionComponent? targetCond) && targetCond.FeintedOnce.Remove(attacker))
        {
            targetCond.FeintedUntil.Remove(attacker);
            _bonuses.RemoveBonusesFrom(target, FeintSource(attacker));
        }

        if (TryComp(attacker, out CFConditionComponent? attackerCond) && attackerCond.OverextendedOnce.Remove(target))
        {
            attackerCond.OverextendedUntil.Remove(target);
            attackerCond.OverextendedPenalty.Remove(target);
        }
    }

    /// <summary>
    /// The circumstance penalty to an attack roll by <paramref name="attacker"/> against <paramref name="target"/>: prone,
    /// or an Overextending Feint. They do not stack with each other or with another circumstance penalty.
    /// </summary>
    public int CircumstanceAttackPenalty(EntityUid attacker, EntityUid target)
    {
        var penalty = IsProne(attacker) ? -2 : 0;
        if (TryComp(attacker, out CFConditionComponent? cond)
            && cond.OverextendedUntil.TryGetValue(target, out var until)
            && _timing.CurTime < until)
        {
            penalty = Math.Min(penalty, -cond.OverextendedPenalty.GetValueOrDefault(target, 2));
        }

        if (penalty == 0)
            return 0;

        var other = TryComp(attacker, out CFBonusesComponent? bonuses)
            ? bonuses.Bonuses.Where(b => b.Stat == CFBonusStat.AttackRoll && b.Type == CFBonusType.Circumstance).Select(b => b.Value).DefaultIfEmpty(0).Min()
            : 0;
        return Math.Min(0, penalty - other);
    }

    private static string FeintSource(EntityUid feinter)
    {
        return $"feint:{feinter}";
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
    /// <param name="ignoreConcealed">The concealed condition does not count (Hunter's Aim). Being hidden still does.</param>
    public bool PassFlatCheck(EntityUid attacker, EntityUid target, bool ignoreConcealed = false)
    {
        if (attacker == target)
            return true;

        var dc = 0;
        if (TryComp(attacker, out CFConditionComponent? blind) && blind.Blinded)
            dc = 11;
        else if (Value(attacker, CFConditionKind.Dazzled) > 0)
            dc = 5;

        // Keener senses (a Whisper Elf's hearing) make the check easier against a creature close enough.
        var sense = CompOrNull<CFSeekSenseComponent>(attacker);
        if (sense != null && !_transform.InRange((attacker, null), (target, null), sense.RangeFeet / CFMelee.FeetPerTile))
            sense = null;

        if (!ignoreConcealed && TryComp(target, out CFConditionComponent? cond) && cond.Concealed)
            dc = Math.Max(dc, sense is { ConcealedFlatDc: > 0 } ? Math.Min(cond.FlatCheckDc, sense.ConcealedFlatDc) : cond.FlatCheckDc);

        if (_stealth.IsUnseenBy(target, attacker))
            dc = Math.Max(dc, sense is { HiddenFlatDc: > 0 } ? sense.HiddenFlatDc : HiddenFlatCheck);

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

    public bool IsProne(EntityUid uid)
    {
        return HasComp<KnockedDownComponent>(uid);
    }

    /// <summary>
    /// −2 circumstance to attack rolls while prone. It does not stack with another circumstance penalty,
    /// such as a loosened grip, so only the part beyond that one counts.
    /// </summary>
    public int ProneAttackPenalty(EntityUid uid)
    {
        if (!IsProne(uid))
            return 0;

        var other = TryComp(uid, out CFBonusesComponent? bonuses)
            ? bonuses.Bonuses.Where(b => b.Stat == CFBonusStat.AttackRoll && b.Type == CFBonusType.Circumstance).Select(b => b.Value).DefaultIfEmpty(0).Min()
            : 0;
        return Math.Min(0, -2 - other);
    }

    /// <summary>Three successful shoves in a row knock the target prone, each within 6 seconds of the last.</summary>
    public void NoteShove(EntityUid target)
    {
        var cond = Ensure(target);
        var now = _timing.CurTime;
        cond.Shoves = now - cond.LastShove <= ShoveWindow ? cond.Shoves + 1 : 1;
        cond.LastShove = now;
        if (cond.Shoves < ShovesToProne)
            return;

        cond.Shoves = 0;
        KnockProne(target);
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

    /// <summary>Holds help for this creature's next check. It fades after a turn if nothing uses it.</summary>
    public void SetAid(EntityUid uid, int bonus)
    {
        var cond = Ensure(uid);
        cond.AidBonus = bonus;
        cond.AidUntil = _timing.CurTime + TimeSpan.FromSeconds(TurnSeconds);
    }

    /// <summary>Uses up any help waiting for this creature, and returns the bonus for this check.</summary>
    public int TakeAid(EntityUid uid)
    {
        if (!TryComp(uid, out CFConditionComponent? cond) || cond.AidBonus == 0)
            return 0;

        var bonus = _timing.CurTime < cond.AidUntil ? cond.AidBonus : 0;
        cond.AidBonus = 0;
        return bonus;
    }

    public bool CanRecall(EntityUid user, EntityUid target)
    {
        return !TryComp(user, out CFConditionComponent? cond) || !cond.Recalled.Contains(target);
    }

    public void NoteRecall(EntityUid user, EntityUid target)
    {
        Ensure(user).Recalled.Add(target);
    }

    public bool IsGrabbing(EntityUid user, EntityUid target)
    {
        return TryComp(target, out CFConditionComponent? cond)
               && cond.Grabbed
               && TryComp(target, out PullableComponent? pullable)
               && pullable.Puller == user;
    }

    public int PerceptionModifier(EntityUid uid)
    {
        return CFDefense.PerceptionModifier(
            _prototypes,
            CompOrNull<CFCombatantComponent>(uid),
            CompOrNull<CFAbilityScoreComponent>(uid),
            DefenseStatus(uid, CFDefenseType.Perception),
            Frightened(uid));
    }

    public int PerceptionDc(EntityUid uid)
    {
        return 10 + PerceptionModifier(uid);
    }

    /// <summary>10 plus the creature's Stealth, for anyone looking for it.</summary>
    public int StealthDc(EntityUid uid)
    {
        if (!TryComp(uid, out CFCombatantComponent? combat) || !TryComp(uid, out CFAbilityScoreComponent? abilities))
            return 10;

        var mod = CFDefense.SkillModifier(_prototypes, combat, abilities, "Stealth", CFAbility.Dexterity, SkillBonus(uid, "Stealth"), Frightened(uid));
        return 10 + mod + _armor.CheckPenalty(uid, "Stealth", CFAbility.Dexterity, false) - Clumsy(uid);
    }

    /// <summary>False if the target cannot be frightened, or has faced down the user within the last minute.</summary>
    public bool CanDemoralize(EntityUid user, EntityUid target)
    {
        if (IsImmune(target, CFConditionKind.Frightened))
            return false;

        return !TryComp(target, out CFConditionComponent? cond)
               || !cond.DemoralizedUntil.TryGetValue(user, out var until)
               || until <= _timing.CurTime;
    }

    /// <summary>Whether a stat block's immunities rule this condition out.</summary>
    public bool IsImmune(EntityUid target, CFConditionKind kind)
    {
        return TryComp(target, out CFConditionImmunityComponent? immunity) && immunity.Conditions.Contains(kind);
    }

    /// <summary>What Demoralize would roll against. Nothing is rolled or changed.</summary>
    /// <summary>
    /// The Outwit edge: +2 circumstance to Deception, Intimidation and Stealth checks against the ranger's prey.
    /// </summary>
    public int OutwitBonus(EntityUid user, EntityUid target)
    {
        return TryComp(user, out CFCombatOptionsComponent? style) && style.PreyOutwit && style.Prey == target ? 2 : 0;
    }

    public bool TryGetDemoralizeCheck(EntityUid user, EntityUid target, out int skill, out int dc, int circumstance = 0)
    {
        skill = 0;
        dc = 0;
        if (IsImmune(target, CFConditionKind.Frightened)
            || !TryComp(user, out CFCombatantComponent? combat)
            || !TryComp(user, out CFAbilityScoreComponent? abilities))
            return false;

        skill = CFDefense.SkillModifier(
            _prototypes,
            combat,
            abilities,
            "Intimidation",
            CFAbility.Charisma,
            Math.Max(circumstance, OutwitBonus(user, target)) + SkillBonus(user, "Intimidation"),
            Frightened(user));

        // Versatile Performance: a performance frightens as well as a threat.
        if (CompOrNull<CFCombatOptionsComponent>(user)?.VersatilePerformance == true)
        {
            skill = Math.Max(skill, CFDefense.SkillModifier(
                _prototypes,
                combat,
                abilities,
                "Performance",
                CFAbility.Charisma,
                Math.Max(circumstance, OutwitBonus(user, target)) + SkillBonus(user, "Performance"),
                Frightened(user)));
        }
        dc = 10 + CFDefense.SaveModifier(
            _prototypes,
            CompOrNull<CFCombatantComponent>(target),
            CompOrNull<CFAbilityScoreComponent>(target),
            CFDefenseType.Will,
            DefenseStatus(target, CFDefenseType.Will, DemoralizeTraits),
            Frightened(target));
        return true;
    }

    /// <summary>What Feint would roll against. Nothing is rolled or changed.</summary>
    public bool TryGetFeintCheck(EntityUid user, EntityUid target, out int skill, out int dc)
    {
        skill = 0;
        dc = 0;
        if (!TryComp(user, out CFCombatantComponent? combat) || !TryComp(user, out CFAbilityScoreComponent? abilities))
            return false;

        skill = CFDefense.SkillModifier(
            _prototypes,
            combat,
            abilities,
            "Deception",
            CFAbility.Charisma,
            OutwitBonus(user, target) + SkillBonus(user, "Deception"),
            Frightened(user));
        dc = 10 + CFDefense.PerceptionModifier(
            _prototypes,
            CompOrNull<CFCombatantComponent>(target),
            CompOrNull<CFAbilityScoreComponent>(target),
            DefenseStatus(target, CFDefenseType.Perception, FeintTraits),
            Frightened(target));
        return true;
    }

    /// <summary>A d20 against a DC with a skill modifier already worked out, with the creature's reactions to a bad roll.</summary>
    private CFDegree RollSkillCheck(EntityUid user, string skill, int modifier, int dc)
    {
        var d20 = _random.Next(1, 21);
        var total = d20 + modifier;
        return _reactions.AnswerSkillCheck(user, skill, d20, modifier, dc, ref total, CFDegrees.DegreeOfSuccess(d20, modifier, dc));
    }

    public bool TryDemoralize(EntityUid user, EntityUid target, int circumstance = 0)
    {
        if (!TryGetDemoralizeCheck(user, target, out var skill, out var dc, circumstance))
            return false;

        var cond = Ensure(target);
        if (cond.DemoralizedUntil.TryGetValue(user, out var until) && until > _timing.CurTime)
        {
            _popup.PopupEntity(Loc.GetString("cf-act-demoralize-immune"), user, user);
            return false;
        }

        cond.DemoralizedUntil[user] = _timing.CurTime + TimeSpan.FromSeconds(60);
        var degree = RollSkillCheck(user, "Intimidation", skill, dc);
        if (degree is CFDegree.Success or CFDegree.CriticalSuccess)
        {
            ApplyFrightened(target, degree == CFDegree.CriticalSuccess ? 2 : 1);
            GainPanache(user);
            return true;
        }

        _popup.PopupEntity(Loc.GetString("cf-act-demoralize-fail"), user, user);
        return false;
    }

    /// <summary>Gives panache to a creature with the Panache class feature.</summary>
    public void GainPanache(EntityUid user)
    {
        if (!TryComp(user, out CFCombatOptionsComponent? style) || !style.PanacheStyle || style.Panache)
            return;

        style.Panache = true;
        Dirty(user, style);
        _popup.PopupEntity(Loc.GetString("cf-act-panache"), user, user);
    }

    /// <summary>Gives panache for a successful Athletics maneuver, to a style that gains it that way.</summary>
    public void GainManeuverPanache(EntityUid user)
    {
        if (CompOrNull<CFCombatOptionsComponent>(user)?.PanacheManeuvers == true)
            GainPanache(user);
    }

    /// <param name="overextending">
    /// An Overextending Feint: instead of off-guard, the target takes -2 circumstance to its next attack roll against the
    /// user, or to all of them for 6 seconds on a critical success.
    /// </param>
    public bool TryFeint(EntityUid user, EntityUid target, bool overextending = false)
    {
        if (!TryGetFeintCheck(user, target, out var skill, out var dc))
            return false;

        var degree = RollSkillCheck(user, "Deception", skill, dc);
        if (degree is not (CFDegree.Success or CFDegree.CriticalSuccess))
        {
            if (degree == CFDegree.CriticalFailure)
                SetOffGuardTo(user, target, TurnSeconds);

            _popup.PopupEntity(Loc.GetString("cf-act-feint-fail"), user, user);
            return false;
        }

        var critical = degree == CFDegree.CriticalSuccess;
        if (overextending)
            Overextend(target, user, once: !critical);
        else
            ApplyFeint(user, target, critical);

        GainPanache(user);
        _popup.PopupEntity(Loc.GetString("cf-act-feint"), target);
        return true;
    }

    /// <summary>
    /// A Feint landed. A success leaves the target off-guard to the user's next melee attack within 6 seconds, and a
    /// critical success to all of them for 6 seconds. A Scoundrel gets more: 6 seconds on a success, every creature's melee
    /// attacks on a critical success, and a free Step after it with an agile or finesse weapon.
    /// </summary>
    public void ApplyFeint(EntityUid user, EntityUid target, bool critical)
    {
        var options = CompOrNull<CFCombatOptionsComponent>(user);
        var scoundrel = options?.ScoundrelFeint == true;
        var cond = Ensure(target);
        var until = _timing.CurTime + TimeSpan.FromSeconds(TurnSeconds);
        if (scoundrel && critical)
        {
            if (until > cond.FeintedAllUntil)
                cond.FeintedAllUntil = until;
        }

        cond.FeintedUntil[user] = until;
        if (critical || scoundrel)
            cond.FeintedOnce.Remove(user);
        else
            cond.FeintedOnce.Add(user);

        if (options?.DistractingFeint == true)
        {
            var source = FeintSource(user);
            _bonuses.RemoveBonusesFrom(target, source);
            _bonuses.AddBonus(target, CFBonusStat.Perception, CFBonusType.Circumstance, -2, TurnSeconds, source: source);
            _bonuses.AddBonus(target, CFBonusStat.Reflex, CFBonusType.Circumstance, -2, TurnSeconds, source: source);
        }

        if (scoundrel && HoldsAgileOrFinesse(user))
            _reactions.CoverSteps(user, StepAfterFeintSeconds);
    }

    /// <summary>How long the Scoundrel's free Step after a Feint lasts.</summary>
    private const float StepAfterFeintSeconds = 2f;

    private bool HoldsAgileOrFinesse(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!HasComp<CFMeleeWeaponComponent>(held))
                continue;

            var traits = _weapons.Traits(held);
            if (traits.Agile || traits.Finesse)
                return true;
        }

        return false;
    }

    /// <summary>The attacker takes -2 circumstance to attack rolls against <paramref name="against"/>, once or for 6 seconds.</summary>
    /// <param name="penalty">How much, as a positive number. 2 for Overextending Feint, 1 for Charming Push.</param>
    public void Overextend(EntityUid attacker, EntityUid against, bool once, int penalty = 2)
    {
        var cond = Ensure(attacker);
        cond.OverextendedUntil[against] = _timing.CurTime + TimeSpan.FromSeconds(TurnSeconds);
        if (penalty == 2)
            cond.OverextendedPenalty.Remove(against);
        else
            cond.OverextendedPenalty[against] = penalty;

        if (once)
            cond.OverextendedOnce.Add(against);
        else
            cond.OverextendedOnce.Remove(against);
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

        var mod = CFDefense.SkillModifier(_prototypes, combat, abilities, skill, ability, itemBonus + SkillBonus(user, skill), Frightened(user));
        mod += _armor.CheckPenalty(user, skill, ability, attackTrait) + TakeAid(user);
        if (ability == CFAbility.Strength)
            mod -= Value(user, CFConditionKind.Enfeebled);

        if (ability == CFAbility.Dexterity)
            mod -= Clumsy(user);

        var dc = 10 + CFDefense.SaveModifier(
            _prototypes,
            CompOrNull<CFCombatantComponent>(target),
            CompOrNull<CFAbilityScoreComponent>(target),
            defense,
            DefenseStatus(target, defense),
            Frightened(target));

        // An Athletics check with the attack trait is a maneuver: Shove, Trip, Disarm, Grapple, Reposition.
        if (attackTrait)
        {
            mod += _bonuses.Total(user, CFBonusStat.Maneuvers);
            dc += _bonuses.Total(target, CFBonusStat.ManeuverDc);
        }

        var d20 = _random.Next(1, 21);
        var total = d20 + mod;
        return _reactions.AnswerSkillCheck(user, skill, d20, mod, dc, ref total, CFDegrees.DegreeOfSuccess(d20, mod, dc));
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
            mod = CFDefense.SkillModifier(_prototypes, combat, abilities, skill, ability, itemBonus + SkillBonus(user, skill), Frightened(user));
        else
            mod -= Frightened(user);

        mod += _armor.CheckPenalty(user, skill, ability, attackTrait) + TakeAid(user);
        if (ability == CFAbility.Strength)
            mod -= Value(user, CFConditionKind.Enfeebled);

        if (ability == CFAbility.Dexterity)
            mod -= Clumsy(user);

        var d20 = _random.Next(1, 21);
        total = d20 + mod;
        return _reactions.AnswerSkillCheck(user, skill, d20, mod, dc, ref total, CFDegrees.DegreeOfSuccess(d20, mod, dc));
    }

    /// <summary>
    /// Clumsy value: a penalty to AC, Reflex saves, ranged attack rolls, and Dexterity-based checks.
    /// Encumbered creatures are clumsy 1.
    /// </summary>
    public int Clumsy(EntityUid uid)
    {
        var timed = TryComp(uid, out CFConditionComponent? cond) ? cond.ClumsyValue : 0;
        return Math.Max(timed, _encumbrance.IsEncumbered(uid) ? 1 : 0);
    }

    /// <summary>
    /// Rolls a save or Perception against a DC. Every save in the game comes through here, so bonuses against a trait
    /// and upgrades to the degree apply to all of them. <paramref name="total"/> is the d20 plus modifier.
    /// </summary>
    /// <param name="traits">What is being resisted: the source's traits, such as Fear or Poison.</param>
    public CFDegree RollDefense(
        EntityUid uid,
        CFDefenseType defense,
        int dc,
        out int total,
        HashSet<ProtoId<CFTraitPrototype>>? traits = null)
    {
        // A thrall fails every check.
        if (HasComp<CFThrallComponent>(uid))
        {
            total = 0;
            return CFDegree.Failure;
        }

        var mod = DefenseModifier(uid, defense, traits);
        var d20 = _random.Next(1, 21);
        if (_bonuses.TryConsumeSaveFortune(uid))
            d20 = Math.Max(d20, _random.Next(1, 21));

        total = d20 + mod;
        var degree = _reactions.AnswerSave(uid, defense, d20, mod, dc, ref total, CFDegrees.DegreeOfSuccess(d20, mod, dc), traits);
        return _bonuses.UpgradeSave(uid, defense, degree, traits);
    }

    /// <summary>A save or Perception as a roll would use it right now.</summary>
    public int DefenseModifier(EntityUid uid, CFDefenseType defense, HashSet<ProtoId<CFTraitPrototype>>? traits = null)
    {
        return CFDefense.SaveModifier(
            _prototypes,
            CompOrNull<CFCombatantComponent>(uid),
            CompOrNull<CFAbilityScoreComponent>(uid),
            defense,
            DefenseStatus(uid, defense, traits),
            Frightened(uid));
    }

    /// <summary>
    /// Everything on a creature that moves its AC from <see cref="CFCombatantComponent.ArmorClass"/>, except off-guard,
    /// which depends on who is attacking.
    /// </summary>
    public int ArmorClassModifier(EntityUid uid)
    {
        // Rage has no AC penalty: that was the legacy rule, and Player Core 2 dropped it.
        return _bonuses.Total(uid, CFBonusStat.ArmorClass) + _shields.RaisedBonus(uid) - Frightened(uid)
               - Math.Max(Clumsy(uid), Value(uid, CFConditionKind.Fatigued) > 0 ? 1 : 0);
    }

    /// <summary>Bonuses to one skill: those to all skills, and those to this skill alone.</summary>
    public int SkillBonus(EntityUid uid, string skill)
    {
        return _bonuses.Total(uid, CFBonusStat.SkillChecks, skill: skill) - (Value(uid, CFConditionKind.Fascinated) > 0 ? 2 : 0);
    }

    /// <summary>Stupefied value: a penalty to Will saves and Perception.</summary>
    public int Stupefied(EntityUid uid)
    {
        return TryComp(uid, out CFConditionComponent? cond) ? cond.StupefiedValue : 0;
    }

    private void ApplyClumsy(EntityUid target, int value, float seconds)
    {
        var cond = EnsureComp<CFConditionComponent>(target);
        cond.ClumsyValue = Math.Max(cond.ClumsyValue, value);
        cond.ClumsyUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
    }

    private void ApplyStupefied(EntityUid target, int value, float seconds)
    {
        var cond = EnsureComp<CFConditionComponent>(target);
        cond.StupefiedValue = Math.Max(cond.StupefiedValue, value);
        cond.StupefiedUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);
    }

    private void ApplySickened(EntityUid target, int value, float seconds)
    {
        value = _reactions.AnswerSickened(target, value);
        if (value <= 0)
            return;

        var cond = EnsureComp<CFConditionComponent>(target);
        cond.SickenedValue = Math.Max(cond.SickenedValue, value);
        cond.SickenedUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        Dirty(target, cond);

        _bonuses.AddBonus(target, CFBonusStat.ArmorClass, CFBonusType.Status, -value, seconds);
        _bonuses.AddBonus(target, CFBonusStat.AttackRoll, CFBonusType.Status, -value, seconds);
        _bonuses.AddBonus(target, CFBonusStat.Saves, CFBonusType.Status, -value, seconds);
        _bonuses.AddBonus(target, CFBonusStat.Perception, CFBonusType.Status, -value, seconds);
        _bonuses.AddBonus(target, CFBonusStat.SkillChecks, CFBonusType.Status, -value, seconds);
    }

    public void KnockProne(EntityUid target)
    {
        // Players stand up themselves, which costs an action. Anyone else keeps stock auto-stand.
        _stun.TryKnockdown(target, TimeSpan.FromSeconds(3), refresh: true, autoStand: !HasComp<ActorComponent>(target), drop: false, force: true);
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
        if (IsImmune(target, kind))
            return;

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
                var asleep = new CFPutToSleepEvent();
                RaiseLocalEvent(target, ref asleep);
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
            case CFConditionKind.Clumsy:
                ApplyClumsy(target, value, seconds);
                break;
            case CFConditionKind.Stupefied:
                ApplyStupefied(target, value, seconds);
                break;
            case CFConditionKind.Sickened:
                ApplySickened(target, value, seconds);
                break;
            case CFConditionKind.Stunned:
                // Stunned is lost actions, taken at once.
                for (var i = 0; i < Math.Max(1, value); i++)
                {
                    _actions.TrySpendActions(target, 1, stealthy: true);
                }

                break;
            case CFConditionKind.Enfeebled:
            case CFConditionKind.Drained:
            case CFConditionKind.Doomed:
            case CFConditionKind.Dazzled:
            case CFConditionKind.Fatigued:
            case CFConditionKind.Deafened:
            case CFConditionKind.Fascinated:
            case CFConditionKind.HealingHalved:
                ApplyValue(target, kind, value, seconds);
                break;
        }
    }

    /// <summary>The value of one of the conditions kept in <see cref="CFConditionComponent.Values"/>, or 0 without it.</summary>
    public int Value(EntityUid uid, CFConditionKind kind)
    {
        return TryComp(uid, out CFConditionComponent? cond) && cond.Values.TryGetValue(kind, out var value) ? value : 0;
    }

    private void ApplyValue(EntityUid target, CFConditionKind kind, int value, float seconds)
    {
        var cond = EnsureComp<CFConditionComponent>(target);
        cond.Values[kind] = Math.Max(Math.Max(1, value), cond.Values.GetValueOrDefault(kind));
        var until = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, seconds));
        if (!cond.ValueUntil.TryGetValue(kind, out var old) || until > old)
            cond.ValueUntil[kind] = until;

        Dirty(target, cond);
    }

    /// <summary>
    /// Ends a condition early, as a successful counteract does. False for one that cannot be ended this way yet: asleep,
    /// paralyzed, sickened and darkvision.
    /// </summary>
    public bool EndCondition(EntityUid target, CFConditionKind kind)
    {
        if (!TryComp(target, out CFConditionComponent? cond))
            return false;

        switch (kind)
        {
            case CFConditionKind.Frightened:
                cond.Frightened = 0;
                break;
            case CFConditionKind.Immobilized:
                cond.Immobilized = false;
                cond.ImmobilizedUntil = TimeSpan.Zero;
                _speed.RefreshMovementSpeedModifiers(target);
                break;
            case CFConditionKind.Concealed:
            case CFConditionKind.Invisible:
                cond.Concealed = false;
                cond.FlatCheckDc = 0;
                cond.ConcealBreaks = false;
                cond.ConcealedUntil = TimeSpan.Zero;
                break;
            case CFConditionKind.Sanctuary:
                cond.Sanctuary = false;
                cond.SanctuaryUntil = TimeSpan.Zero;
                break;
            case CFConditionKind.Charmed:
                cond.Charmed = false;
                cond.Charmer = null;
                cond.CharmedUntil = TimeSpan.Zero;
                break;
            case CFConditionKind.Calmed:
                cond.Calmed = false;
                cond.CalmedUntil = TimeSpan.Zero;
                break;
            case CFConditionKind.Blinded:
                cond.Blinded = false;
                cond.BlindUntil = TimeSpan.Zero;
                break;
            case CFConditionKind.OffGuard:
                cond.OffGuardAll = false;
                cond.OffGuardAllUntil = TimeSpan.Zero;
                break;
            case CFConditionKind.Clumsy:
                cond.ClumsyValue = 0;
                cond.ClumsyUntil = TimeSpan.Zero;
                break;
            case CFConditionKind.Enfeebled:
            case CFConditionKind.Drained:
            case CFConditionKind.Doomed:
            case CFConditionKind.Dazzled:
            case CFConditionKind.Fatigued:
            case CFConditionKind.Deafened:
            case CFConditionKind.Fascinated:
            case CFConditionKind.HealingHalved:
                cond.Values.Remove(kind);
                cond.ValueUntil.Remove(kind);
                break;
            case CFConditionKind.Stupefied:
                cond.StupefiedValue = 0;
                cond.StupefiedUntil = TimeSpan.Zero;
                break;
            case CFConditionKind.SpellShield:
                cond.ShieldHardness = 0;
                break;
            default:
                return false;
        }

        Dirty(target, cond);
        return true;
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

    private int DefenseStatus(EntityUid uid, CFDefenseType defense, HashSet<ProtoId<CFTraitPrototype>>? traits = null)
    {
        var status = defense switch
        {
            CFDefenseType.Perception => _bonuses.Total(uid, CFBonusStat.Perception, traits: traits),
            CFDefenseType.Reflex => _bonuses.Total(uid, CFBonusStat.Saves, CFBonusStat.Reflex, traits),
            CFDefenseType.Will => _bonuses.Total(uid, CFBonusStat.Saves, CFBonusStat.Will, traits),
            _ => _bonuses.Total(uid, CFBonusStat.Saves, CFBonusStat.Fortitude, traits),
        };

        // Fatigued lowers every save, and deafened and fascinated lower Perception. The worst of them counts, once.
        var extra = defense == CFDefenseType.Perception
            ? Math.Max(Value(uid, CFConditionKind.Deafened) > 0 ? 2 : 0, Value(uid, CFConditionKind.Fascinated) > 0 ? 2 : 0)
            : Value(uid, CFConditionKind.Fatigued) > 0 ? 1 : 0;

        return defense switch
        {
            CFDefenseType.Reflex => status - Math.Max(Clumsy(uid), extra),
            CFDefenseType.Will => status - Math.Max(Stupefied(uid), extra),
            CFDefenseType.Perception => status - Math.Max(Stupefied(uid), extra),
            CFDefenseType.Fortitude => status - Math.Max(Value(uid, CFConditionKind.Drained), extra),
            _ => status,
        };
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
