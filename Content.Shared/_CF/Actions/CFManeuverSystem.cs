using Content.Shared.CF.Items;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Pointing;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.CF.DamageTypes;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

public enum CFManeuver : byte
{
    None,
    Shove,
    Disarm,
}

/// <summary>
/// Maneuvers done with the hands instead of the action wheel: right-click Shove and Disarm, pulling to Grapple,
/// moving to Escape, pointing to mark or Feint, and the cost of standing up.
/// </summary>
public sealed partial class CFManeuverSystem : EntitySystem
{
    /// <summary>The old Hunt Prey action's range, in tiles.</summary>
    private const float MarkRange = 12f;

    private static readonly TimeSpan EscapeInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan EscapeBlocked = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan NpcEscapeInterval = TimeSpan.FromSeconds(6);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFActivitySystem _activities = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFImplementSystem _implements = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;

    private bool HasStats(EntityUid uid)
    {
        return HasComp<CFCombatantComponent>(uid) && HasComp<CFAbilityScoreComponent>(uid);
    }

    /// <summary>
    /// What a right-click does. A shield always Shoves. An empty hand, or a weapon with both traits, Disarms
    /// someone holding something and Shoves anyone else, as stock's disarm does.
    /// </summary>
    public CFManeuver Decide(EntityUid user, EntityUid target)
    {
        var targetHolds = _hands.TryGetActiveItem(target, out _);
        if (!_hands.TryGetActiveItem(user, out var held) || held is not { } item)
            return targetHolds ? CFManeuver.Disarm : CFManeuver.Shove;

        if (HasComp<CFShieldComponent>(item))
            return CFManeuver.Shove;

        var traits = _weapons.Traits(item);
        if (traits.Sweep)
            return CFManeuver.None;

        if (traits.Shove && traits.Disarm)
            return targetHolds ? CFManeuver.Disarm : CFManeuver.Shove;

        if (traits.Shove)
            return CFManeuver.Shove;

        return traits.Disarm && targetHolds ? CFManeuver.Disarm : CFManeuver.None;
    }

    /// <summary>Takes over stock's right-click before it rolls its own chance to disarm.</summary>
    [SubscribeLocalEvent]
    private void OnDisarmAttempt(Entity<MobStateComponent> ent, ref DisarmAttemptEvent args)
    {
        var user = args.DisarmerUid;
        if (!HasStats(user))
            return;

        args.Cancelled = true;

        // Cancelling skips the end of the attack, so holding the button would repeat the maneuver.
        if (_melee.TryGetWeapon(user, out var weaponUid, out var weapon))
        {
            weapon.Attacking = true;
            DirtyField(weaponUid, weapon, nameof(MeleeWeaponComponent.Attacking));
        }

        if (_net.IsServer)
            TryManeuver(user, ent);
    }

    /// <summary>Pays one action and resolves the right-click maneuver. Returns what was attempted.</summary>
    public CFManeuver TryManeuver(EntityUid user, EntityUid target)
    {
        var maneuver = Decide(user, target);
        if (maneuver == CFManeuver.None)
        {
            _popup.PopupEntity(Loc.GetString("cf-act-nothing-to-disarm"), user, user);
            return maneuver;
        }

        if (!_actions.TrySpendActions(user, 1))
            return CFManeuver.None;

        if (maneuver == CFManeuver.Shove)
            Shove(user, target);
        else
            Disarm(user, target);

        return maneuver;
    }

    private void Shove(EntityUid user, EntityUid target)
    {
        var bonus = _activities.ManeuverBonus(user, t => t.Shove);
        var degree = _conditions.RollSkill(user, target, "Athletics", CFAbility.Strength, CFDefenseType.Fortitude, attackTrait: true, bonus);
        switch (degree)
        {
            case CFDegree.CriticalFailure:
                _conditions.KnockProne(user);
                return;
            case CFDegree.Failure:
                _popup.PopupEntity(Loc.GetString("cf-act-shove-fail"), user, user);
                return;
        }

        _conditions.ReleaseGrab(target);
        _conditions.Push(user, target, degree == CFDegree.CriticalSuccess ? 10 : 5);
        _conditions.GainManeuverPanache(user);
        if (CompOrNull<CFCombatOptionsComponent>(user)?.PunishingShove == true
            && TryComp(user, out CFAbilityScoreComponent? abilities))
        {
            var strength = abilities.GetModifier(CFAbility.Strength);
            if (strength > 0)
                _effects.DealDamage(user, target, degree == CFDegree.CriticalSuccess ? strength * 2 : strength, CFDamageTypes.Bludgeoning, critical: false);
        }

        _popup.PopupEntity(Loc.GetString("cf-act-shove"), target);
        _conditions.NoteShove(target);
    }

    private void Disarm(EntityUid user, EntityUid target)
    {
        var bonus = _activities.ManeuverBonus(user, t => t.Disarm);
        var degree = _conditions.RollSkill(user, target, "Athletics", CFAbility.Strength, CFDefenseType.Reflex, attackTrait: true, bonus);
        switch (degree)
        {
            case CFDegree.CriticalSuccess:
                if (_hands.TryGetActiveItem(target, out var item))
                    _hands.TryDrop(target, item.Value, checkActionBlocker: false);
                _popup.PopupEntity(Loc.GetString("cf-act-disarm-drop"), target);
                break;
            case CFDegree.Success:
                _bonuses.AddBonus(target, CFBonusStat.AttackRoll, CFBonusType.Circumstance, -2, 6f);
                _conditions.GainManeuverPanache(user);
                _popup.PopupEntity(Loc.GetString("cf-act-disarm"), target);
                break;
            case CFDegree.CriticalFailure:
                _conditions.SetOffGuardAll(user, 6f);
                break;
            default:
                _popup.PopupEntity(Loc.GetString("cf-act-disarm-fail"), user, user);
                break;
        }
    }

    /// <summary>
    /// Pulling a living enemy is a Grapple. This is the only pull event with a cost: CanPull also runs for every
    /// verb menu. Friends, allies, the downed and objects pull for free.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPullAttempt(Entity<MobStateComponent> ent, ref PullAttemptEvent args)
    {
        var user = args.PullerUid;
        if (args.PulledUid != ent.Owner || !HasStats(user) || !_mobState.IsAlive(ent, ent.Comp) || !_factions.CanHarm(user, ent))
            return;

        // The client waits for the server to start the pull.
        if (!_net.IsServer)
        {
            args.Cancelled = true;
            return;
        }

        if (!TryGrapple(user, ent))
            args.Cancelled = true;
    }

    public bool TryGrapple(EntityUid user, EntityUid target)
    {
        if (!_actions.TrySpendActions(user, 1))
            return false;

        var bonus = _activities.ManeuverBonus(user, t => t.Grapple);
        var degree = _conditions.RollSkill(user, target, "Athletics", CFAbility.Strength, CFDefenseType.Fortitude, attackTrait: true, bonus);
        if (degree == CFDegree.CriticalFailure)
        {
            _conditions.ApplyGrabbed(user, target);
            return false;
        }

        if (!degree.IsSuccess())
        {
            _popup.PopupEntity(Loc.GetString("cf-act-grapple-fail"), user, user);
            return false;
        }

        _conditions.ApplyGrabbed(target, user);
        _conditions.GainManeuverPanache(user);
        if (CompOrNull<CFCombatOptionsComponent>(user)?.CrushingGrab == true
            && TryComp(user, out CFAbilityScoreComponent? abilities))
        {
            var strength = abilities.GetModifier(CFAbility.Strength);
            if (strength > 0)
                _effects.DealDamage(user, target, strength, CFDamageTypes.Bludgeoning, critical: false);
        }

        return true;
    }

    /// <summary>A grab and its pull last together. Stock would let the creature simply walk out of the pull.</summary>
    [SubscribeLocalEvent]
    private void OnAttemptStopPull(Entity<CFConditionComponent> ent, ref AttemptStopPullingEvent args)
    {
        if (ent.Comp.Grabbed && args.User == ent.Owner)
            args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnPullStopped(Entity<CFConditionComponent> ent, ref PullStoppedMessage args)
    {
        if (args.PulledUid == ent.Owner)
            _conditions.ReleaseGrab(ent);
    }

    [SubscribeLocalEvent]
    private void OnMoveInput(Entity<CFConditionComponent> ent, ref MoveInputEvent args)
    {
        if (_net.IsServer && ent.Comp.Grabbed && args.HasDirectionalMovement)
            TryEscape(ent);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        UpdateTumble();

        // NPC steering raises no move input, so grabbed creatures without a player try once in a while.
        var now = _timing.CurTime;
        var grabbed = EntityQueryEnumerator<CFConditionComponent>();
        while (grabbed.MoveNext(out var uid, out var cond))
        {
            if (!cond.Grabbed || now < cond.NextEscape || HasComp<ActorComponent>(uid))
                continue;

            cond.NextEscape = now + NpcEscapeInterval;
            TryEscape((uid, cond));
        }
    }

    /// <summary>
    /// Escape: one action, the best of unarmed attack, Athletics and Acrobatics against the grabber's Athletics DC.
    /// A critical failure blocks another try for a while.
    /// </summary>
    /// <param name="free">Costs no action and ignores the wait between attempts, as when a champion frees an ally.</param>
    public bool TryEscape(Entity<CFConditionComponent> ent, bool free = false)
    {
        var now = _timing.CurTime;
        if (!free && now < ent.Comp.NextEscape && HasComp<ActorComponent>(ent)
            || ent.Comp.Grabber is not { } grabber
            || !free && !_actions.TrySpendActions(ent, 1))
        {
            return false;
        }

        ent.Comp.NextEscape = now + EscapeInterval;
        var degree = CFDefense.Roll(_random, EscapeModifier(ent), 10 + AthleticsModifier(grabber));
        if (!degree.IsSuccess())
        {
            if (degree == CFDegree.CriticalFailure)
                ent.Comp.NextEscape = now + EscapeBlocked;

            _popup.PopupEntity(Loc.GetString("cf-act-escape-fail"), ent, ent);
            return false;
        }

        _conditions.ReleaseGrab(ent);
        _popup.PopupEntity(Loc.GetString("cf-act-escape"), ent);
        return true;
    }

    private int AthleticsModifier(EntityUid uid)
    {
        return TryComp(uid, out CFCombatantComponent? combat) && TryComp(uid, out CFAbilityScoreComponent? abilities)
            ? CFDefense.SkillModifier(_prototypes, combat, abilities, "Athletics", CFAbility.Strength, _conditions.SkillBonus(uid, "Athletics"))
            : 0;
    }

    private int EscapeModifier(EntityUid uid)
    {
        if (!TryComp(uid, out CFCombatantComponent? combat) || !TryComp(uid, out CFAbilityScoreComponent? abilities))
            return 0;

        var acrobatics = CFDefense.SkillModifier(_prototypes, combat, abilities, "Acrobatics", CFAbility.Dexterity, _conditions.SkillBonus(uid, "Acrobatics"));
        var rank = CFDefense.AttackRank(_prototypes, combat, CFWeaponCategory.Unarmed.Proficiency());
        var unarmed = abilities.GetModifier(CFAbility.Strength) + CFSkills.ProficiencyBonus(combat.Level, rank);
        return Math.Max(Math.Max(AthleticsModifier(uid), acrobatics), unarmed);
    }

    /// <summary>Rangers, Thaumaturges and Investigators mark a living enemy with the stock point gesture.</summary>
    [SubscribeLocalEvent]
    private void OnPointedAt(Entity<CFCombatOptionsComponent> ent, ref AfterPointedAtEvent args)
    {
        if (_net.IsServer)
            PointAt(ent, args.Pointed);
    }

    public bool TryMark(Entity<CFCombatOptionsComponent> ent, EntityUid target)
    {
        var opt = ent.Comp;
        if (opt.PointMark == CFPointMark.None || !_mobState.IsAlive(target) || !_factions.CanHarm(ent, target))
            return false;

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(target);
        if (here.MapId != there.MapId || (here.Position - there.Position).Length() > MarkRange)
            return false;

        var already = opt.PointMark switch
        {
            CFPointMark.Prey => opt.Prey == target,
            CFPointMark.Exploit => opt.Exploited == target,
            _ => false,
        };
        if (already || !_actions.TrySpendActions(ent, 1))
            return false;

        switch (opt.PointMark)
        {
            case CFPointMark.Prey:
                _activities.Mark(ent, target, prey: true);
                break;
            case CFPointMark.Exploit:
                _activities.Mark(ent, target, prey: false);
                break;
            case CFPointMark.Stratagem:
                _activities.Stratagem(ent, target);
                break;
        }

        return true;
    }

    /// <summary>A stand attempt checks the creature could pay. It runs often, so the cost comes when it finishes.</summary>
    [SubscribeLocalEvent]
    private void OnStandAttempt(Entity<CFActionPoolComponent> ent, ref StandUpAttemptEvent args)
    {
        if (!_actions.CanAffordActions(ent, 1))
            args.Cancelled = true;
    }

    [SubscribeLocalEvent(after: [typeof(SharedStunSystem)])]
    private void OnStandFinished(Entity<CFActionPoolComponent> ent, ref TryStandDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        if (!HasComp<KnockedDownComponent>(ent))
        {
            _actions.TrySpendActions(ent, 1);
            return;
        }

        // Stock turns auto-stand off when there is no room. NPCs have nobody to press the key again.
        if (!HasComp<ActorComponent>(ent))
            _stun.SetAutoStand(ent.Owner, true);
    }
}
