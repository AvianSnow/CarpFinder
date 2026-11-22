using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.CombatMode;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Medicine;

/// <summary>A healer's toolkit. Stock healing items change SS14 damage, not CF hit points, so this is its own item.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFHealersToolkitComponent : Component;

/// <summary>Who cannot be treated again yet. Added the first time a creature is treated.</summary>
[RegisterComponent]
public sealed partial class CFPatientComponent : Component
{
    public TimeSpan TreatWoundsUntil;

    /// <summary>Battle Medicine immunity is to one healer. Printed: "immune to your Battle Medicine".</summary>
    public Dictionary<EntityUid, TimeSpan> BattleMedicineUntil = new();
}

/// <summary>On a healer partway through Treat Wounds. Removed when it ends, however it ends.</summary>
[RegisterComponent]
public sealed partial class CFTreatingComponent : Component
{
    public EntityUid Patient;

    public DoAfterId? DoAfter;

    /// <summary>The healer holds the patient still, so the treatment ends if the patient breaks free.</summary>
    public bool HoldsPatient;

    public int ActionsLeft;

    public TimeSpan NextAction;
}

[Serializable, NetSerializable]
public sealed partial class CFTreatWoundsDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Treat Wounds by clicking a creature with the toolkit, and Battle Medicine by using it in hand in combat mode.
/// A click in combat mode is always a swing, so Battle Medicine picks the patient itself.
/// </summary>
public sealed partial class CFMedicineSystem : EntitySystem
{
    private const string Skill = "Medicine";

    /// <summary>10 minutes printed. Short enough to do in a fight, at a cost of 3 actions spread across it.</summary>
    private const float TreatSeconds = 12f;

    private const int TreatActions = 3;

    private const int Dice = 2;
    private const int CriticalDice = 4;
    private const int Die = 8;

    private static readonly TimeSpan TreatImmunity = TimeSpan.FromMinutes(3);

    /// <summary>A day printed. An hour is a whole session of play here.</summary>
    private static readonly TimeSpan BattleMedicineImmunity = TimeSpan.FromHours(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFVfxSystem _vfx = default!;

    private static TimeSpan TreatActionEvery => TimeSpan.FromSeconds(TreatSeconds / TreatActions);

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFTreatingComponent>();
        while (query.MoveNext(out var uid, out var treating))
        {
            if (treating.HoldsPatient && !IsHolding(uid, treating.Patient))
            {
                _doAfter.Cancel(treating.DoAfter);
                continue;
            }

            if (treating.ActionsLeft <= 0 || now < treating.NextAction)
                continue;

            if (!_actions.TrySpendActions(uid, 1))
            {
                _doAfter.Cancel(treating.DoAfter);
                continue;
            }

            treating.ActionsLeft--;
            treating.NextAction += TreatActionEvery;
            _vfx.Healing(treating.Patient);
        }
    }

    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<CFHealersToolkitComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !IsPatient(target))
            return;

        args.Handled = true;
        if (!CanTreat(args.User))
            return;

        if (Comp(target).TreatWoundsUntil > _timing.CurTime)
        {
            _popup.PopupEntity(Loc.GetString("cf-medicine-immune"), target, args.User);
            return;
        }

        TryStartTreatment(ent, args.User, target);
    }

    /// <summary>
    /// Starts Treat Wounds. The healer holds the patient still, and the patient can try to Escape, which ends it.
    /// The first of its actions is paid now and the rest as it goes, so running out of actions also ends it.
    /// </summary>
    public bool TryStartTreatment(EntityUid toolkit, EntityUid user, EntityUid target)
    {
        var position = _transform.GetWorldPosition(user);
        if (target != user && !CFFlanking.InReach(position, _transform.GetWorldPosition(target), CFFlanking.DefaultReachFeet))
        {
            _popup.PopupEntity(Loc.GetString("cf-medicine-too-far"), target, user);
            return false;
        }

        if (!_actions.CanAffordActions(user, 1))
        {
            _popup.PopupEntity(Loc.GetString("cf-action-not-enough"), user, user);
            return false;
        }

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            TreatSeconds,
            new CFTreatWoundsDoAfterEvent(),
            eventTarget: toolkit,
            target: target,
            used: toolkit)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter, out var id) || !_net.IsServer)
            return id != null;

        _actions.TrySpendActions(user, 1);
        var treating = EnsureComp<CFTreatingComponent>(user);
        treating.Patient = target;
        treating.DoAfter = id;
        treating.ActionsLeft = TreatActions - 1;
        treating.NextAction = _timing.CurTime + TreatActionEvery;
        treating.HoldsPatient = target != user && !_conditions.Ensure(target).Grabbed;
        if (treating.HoldsPatient)
            _conditions.ApplyGrabbed(target, user);

        _vfx.Healing(target);
        return true;
    }

    private bool IsHolding(EntityUid healer, EntityUid patient)
    {
        return TryComp(patient, out CFConditionComponent? condition) && condition.Grabbed && condition.Grabber == healer;
    }

    [SubscribeLocalEvent]
    private void OnTreated(Entity<CFHealersToolkitComponent> ent, ref CFTreatWoundsDoAfterEvent args)
    {
        if (_net.IsServer)
            EndTreatment(args.User);

        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        args.Handled = true;
        if (_net.IsServer)
            TreatWounds(args.User, target);
    }

    private void EndTreatment(EntityUid healer)
    {
        if (!TryComp(healer, out CFTreatingComponent? treating))
            return;

        if (treating.HoldsPatient && IsHolding(healer, treating.Patient))
            _conditions.ReleaseGrab(treating.Patient);

        RemCompDeferred<CFTreatingComponent>(healer);
    }

    [SubscribeLocalEvent]
    private void OnUseInHand(Entity<CFHealersToolkitComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !_combatMode.IsInCombatMode(args.User))
            return;

        if (!TryComp(args.User, out CFCombatOptionsComponent? style) || !style.BattleMedicine)
            return;

        args.Handled = true;
        if (_net.IsServer && TryNearestPatient(args.User, out var patient))
            TryBattleMedicine(args.User, patient);
    }

    /// <summary>The closest hurt ally in reach who can still be helped, or the user.</summary>
    public bool TryNearestPatient(EntityUid user, out EntityUid patient)
    {
        patient = default;
        var here = _transform.GetMapCoordinates(user);
        var best = float.MaxValue;
        foreach (var mob in _lookup.GetEntitiesInRange<CFCombatantComponent>(here, CFMelee.MeleeRange))
        {
            CheckPatient(user, mob, here.Position, ref patient, ref best);
        }

        if (TryComp(user, out CFCombatantComponent? self))
            CheckPatient(user, (user, self), here.Position, ref patient, ref best);

        return best < float.MaxValue;
    }

    private void CheckPatient(
        EntityUid user,
        Entity<CFCombatantComponent> mob,
        System.Numerics.Vector2 from,
        ref EntityUid patient,
        ref float best)
    {
        if (mob.Comp.CurrentHp >= mob.Comp.MaxHp
            || !IsPatient(mob)
            || (mob.Owner != user && _factions.CanHarm(user, mob))
            || IsImmuneToBattleMedicine(user, mob))
            return;

        var distance = (_transform.GetMapCoordinates(mob).Position - from).Length();
        if (distance >= best)
            return;

        best = distance;
        patient = mob;
    }

    /// <summary>The check at the end of Treat Wounds. Higher ranks take a harder DC for more healing.</summary>
    public CFDegree TreatWounds(EntityUid user, EntityUid target)
    {
        var (dc, bonus) = Dcs(user);
        var degree = _conditions.RollSkillAgainst(user, Skill, CFAbility.Wisdom, dc, out _);
        Comp(target).TreatWoundsUntil = _timing.CurTime + TreatImmunity;

        if (!ApplyResult(user, target, degree, bonus))
            return degree;

        if (TryComp(target, out CFCombatantComponent? combat) && combat.Wounded != 0)
        {
            combat.Wounded = 0;
            Dirty(target, combat);
        }

        return degree;
    }

    /// <summary>One action. The same check as Treat Wounds, but it leaves the wounded condition.</summary>
    public bool TryBattleMedicine(EntityUid user, EntityUid target)
    {
        if (!_net.IsServer || !CanTreat(user))
            return false;

        if (IsImmuneToBattleMedicine(user, target))
        {
            _popup.PopupEntity(Loc.GetString("cf-medicine-immune"), target, user);
            return false;
        }

        if (!_actions.TrySpendActions(user, 1))
            return false;

        var (dc, bonus) = Dcs(user);
        var degree = _conditions.RollSkillAgainst(user, Skill, CFAbility.Wisdom, dc, out _);
        Comp(target).BattleMedicineUntil[user] = _timing.CurTime + BattleMedicineImmunity;
        return ApplyResult(user, target, degree, bonus);
    }

    private bool ApplyResult(EntityUid user, EntityUid target, CFDegree degree, int bonus)
    {
        switch (degree)
        {
            case CFDegree.CriticalSuccess:
                _effects.Heal(user, target, RollDice(CriticalDice) + bonus);
                return true;
            case CFDegree.Success:
                _effects.Heal(user, target, RollDice(Dice) + bonus);
                return true;
            case CFDegree.CriticalFailure:
                _effects.DealDamage(user, target, RollDice(1), "Slashing", false);
                _popup.PopupEntity(Loc.GetString("cf-medicine-harm"), target, user);
                return false;
            default:
                _popup.PopupEntity(Loc.GetString("cf-medicine-fail"), target, user);
                return false;
        }
    }

    private int RollDice(int dice)
    {
        var total = 0;
        for (var i = 0; i < dice; i++)
        {
            total += _random.Next(1, Die + 1);
        }

        return total;
    }

    /// <summary>The DC and extra healing for this healer's rank.</summary>
    private (int Dc, int Bonus) Dcs(EntityUid user)
    {
        var rank = TryComp(user, out CFCombatantComponent? combat)
            ? CFDefense.SkillRank(_prototypes, combat, Skill)
            : CFProficiencyRank.Untrained;

        return rank switch
        {
            CFProficiencyRank.Legendary => (40, 50),
            CFProficiencyRank.Master => (30, 30),
            CFProficiencyRank.Expert => (20, 10),
            _ => (15, 0),
        };
    }

    private bool CanTreat(EntityUid user)
    {
        if (TryComp(user, out CFCombatantComponent? combat)
            && CFDefense.SkillRank(_prototypes, combat, Skill) >= CFProficiencyRank.Trained)
            return true;

        _popup.PopupEntity(Loc.GetString("cf-medicine-untrained"), user, user);
        return false;
    }

    private bool IsPatient(EntityUid target)
    {
        return HasComp<CFCombatantComponent>(target) && HasComp<MobStateComponent>(target) && !_mobState.IsDead(target);
    }

    private bool IsImmuneToBattleMedicine(EntityUid healer, EntityUid target)
    {
        return TryComp(target, out CFPatientComponent? patient)
               && patient.BattleMedicineUntil.GetValueOrDefault(healer) > _timing.CurTime;
    }

    private CFPatientComponent Comp(EntityUid target)
    {
        return EnsureComp<CFPatientComponent>(target);
    }
}
