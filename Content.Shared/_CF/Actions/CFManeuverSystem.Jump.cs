using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Input;
using Content.Shared.CF.Preferences;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Trip, High Jump and Long Jump. They have keys of their own (<see cref="CFKeyFunctions"/>) and no button: Trip goes
/// for the enemy being aimed at, and a jump goes the way the creature faces, as Leap does.
/// </summary>
public sealed partial class CFManeuverSystem
{
    /// <summary>What a High Jump has to beat.</summary>
    public const int HighJumpDc = 30;

    public override void Initialize()
    {
        base.Initialize();
        CommandBinds.Builder
            .Bind(CFKeyFunctions.CFTrip, InputCmdHandler.FromDelegate(session => OnKey(session, CFBasicAction.Trip), handle: false))
            .Bind(CFKeyFunctions.CFHighJump, InputCmdHandler.FromDelegate(session => OnKey(session, CFBasicAction.HighJump), handle: false))
            .Bind(CFKeyFunctions.CFLongJump, InputCmdHandler.FromDelegate(session => OnKey(session, CFBasicAction.LongJump), handle: false))
            .Register<CFManeuverSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<CFManeuverSystem>();
    }

    private void OnKey(ICommonSession? session, CFBasicAction action)
    {
        if (!_net.IsServer || session?.AttachedEntity is not { } user || !HasStats(user))
            return;

        if (action != CFBasicAction.Trip)
        {
            PerformBasic(user, action, null);
            return;
        }

        if (!TryAimedStrikeTarget(user, out var target))
        {
            _popup.PopupEntity(Loc.GetString("cf-act-trip-nobody"), user, user);
            return;
        }

        PerformBasic(user, action, target);
    }

    #region Trip

    /// <summary>A Trip needs a free hand, or a held weapon with the trip trait, and the target in its reach and standing.</summary>
    public bool CanTrip(EntityUid user, EntityUid target)
    {
        if (_conditions.IsProne(target))
            return false;

        var reach = CFFlanking.DefaultReachFeet;
        var tripWeapon = false;
        if (_hands.TryGetActiveItem(user, out var held) && _weapons.Traits(held.Value).Trip)
        {
            tripWeapon = true;
            reach = _weapons.ReachFeet(held.Value);
        }

        return (tripWeapon || _hands.CountFreeHands(user) > 0) && InReach(user, target, reach);
    }

    /// <summary>One action: Athletics against the target's Reflex DC.</summary>
    public bool TryTrip(EntityUid user, EntityUid target)
    {
        if (!_net.IsServer || !CanTrip(user, target) || !_actions.TrySpendActions(user, 1))
            return false;

        return Trip(user, target);
    }

    /// <summary>
    /// Resolves a Trip that is already paid for. A success knocks the target prone, a critical success also deals 1d6
    /// bludgeoning damage, and a critical failure puts the user on the ground.
    /// </summary>
    private bool Trip(EntityUid user, EntityUid target)
    {
        var bonus = _activities.ManeuverBonus(user, t => t.Trip);
        var degree = _conditions.RollSkill(user, target, "Athletics", CFAbility.Strength, CFDefenseType.Reflex, attackTrait: true, bonus);
        switch (degree)
        {
            case CFDegree.CriticalSuccess:
                _conditions.ReleaseGrab(target);
                _conditions.KnockProne(target);
                _effects.DealDamage(user, target, _effects.Roll(1, 6, 0), "Bludgeoning", critical: false);
                _conditions.GainManeuverPanache(user);
                _popup.PopupEntity(Loc.GetString("cf-act-trip"), target);
                return true;
            case CFDegree.Success:
                _conditions.KnockProne(target);
                _conditions.GainManeuverPanache(user);
                _popup.PopupEntity(Loc.GetString("cf-act-trip"), target);
                return true;
            case CFDegree.CriticalFailure:
                _conditions.KnockProne(user);
                return false;
            default:
                _popup.PopupEntity(Loc.GetString("cf-act-trip-fail"), user, user);
                return false;
        }
    }

    #endregion

    #region Monster follow-ups

    /// <summary>
    /// Monster Core's Grab, Knockdown and Push: after a Strike that lists one lands, the monster Grapples, Trips or
    /// Shoves with that body part for 1 action, or none for the improved version. It needs no free hand. A Grab holds
    /// for 6 seconds, the monster's next turn, and a critical success restrains.
    /// </summary>
    public bool FollowUp(EntityUid user, EntityUid target, CFFollowUp kind, bool free)
    {
        if (!_net.IsServer || kind == CFFollowUp.None || !_mobState.IsAlive(target))
            return false;

        if (kind == CFFollowUp.Knockdown && _conditions.IsProne(target)
            || kind == CFFollowUp.Grab && CompOrNull<CFConditionComponent>(target)?.Grabbed == true)
        {
            return false;
        }

        if (!free && !_actions.TrySpendActions(user, 1))
            return false;

        switch (kind)
        {
            case CFFollowUp.Knockdown:
                return Trip(user, target);
            case CFFollowUp.Push:
                Shove(user, target);
                return true;
            default:
                var degree = _conditions.RollSkill(user, target, "Athletics", CFAbility.Strength, CFDefenseType.Fortitude, attackTrait: true);
                if (!degree.IsSuccess())
                    return false;

                _conditions.ApplyGrabbed(target, user, 6f);
                if (degree == CFDegree.CriticalSuccess && CompOrNull<CFConditionComponent>(target)?.Grabbed == true)
                    _conditions.ApplyCondition(target, CFConditionKind.Restrained, 1, 6f, user, 0);

                return true;
        }
    }

    #endregion

    #region Jumps

    /// <summary>A jump costs 2 actions, or 1 with Quick Jump.</summary>
    public int JumpActions(EntityUid user)
    {
        return CompOrNull<CFCombatOptionsComponent>(user)?.QuickJump == true ? 1 : 2;
    }

    private bool CanJump(EntityUid user)
    {
        return !_conditions.IsProne(user)
               && TryComp(user, out CFCombatantComponent? combat)
               && CFActivitySystem.LeapTiles(combat.SpeedFeet) > 0
               && _actions.CanAffordActions(user, JumpActions(user));
    }

    /// <summary>How far a plain Leap carries this creature, in feet.</summary>
    private int LeapFeet(EntityUid user)
    {
        var tiles = CFActivitySystem.LeapTiles(Comp<CFCombatantComponent>(user).SpeedFeet);
        var options = CompOrNull<CFCombatOptionsComponent>(user);
        if (options?.DancingLeaf == true)
            tiles++;

        if (options?.PowerfulLeap == true)
            tiles++;

        return tiles * 5;
    }

    /// <summary>
    /// Long Jump: Athletics, and the creature jumps the way it faces as far as the roll reaches, 5 feet for every 5
    /// rolled, up to its Speed. A roll that falls short is a plain Leap, and a critical failure ends prone.
    /// The rules have the jumper name a distance first and Stride 10 feet: here the roll sets the distance.
    /// </summary>
    public bool TryLongJump(EntityUid user)
    {
        if (!_net.IsServer || !CanJump(user) || !_actions.TrySpendActions(user, JumpActions(user)))
            return false;

        var leap = LeapFeet(user);
        var speed = Comp<CFCombatantComponent>(user).SpeedFeet;
        var degree = _conditions.RollSkillAgainst(user, "Athletics", CFAbility.Strength, leap + 5, out var total);
        var feet = leap;
        if (degree.IsSuccess())
        {
            var most = speed + (CompOrNull<CFCombatOptionsComponent>(user)?.PowerfulLeap == true ? 5 : 0);
            feet = Math.Clamp(total / 5 * 5, leap, Math.Max(leap, most));
        }

        _activities.LeapBy(user, feet / 5f);
        _popup.PopupEntity(Loc.GetString("cf-act-long-jump", ("feet", feet)), user, user);
        if (degree == CFDegree.CriticalFailure)
            _conditions.KnockProne(user);

        return degree.IsSuccess();
    }

    /// <summary>
    /// High Jump: Athletics against DC 30. A success clears 5 feet straight up and a critical success 8, landing a
    /// tile on. A failure is a plain Leap, and a critical failure only falls over.
    /// </summary>
    public bool TryHighJump(EntityUid user)
    {
        if (!_net.IsServer || !CanJump(user) || !_actions.TrySpendActions(user, JumpActions(user)))
            return false;

        var degree = _conditions.RollSkillAgainst(user, "Athletics", CFAbility.Strength, HighJumpDc, out _);
        switch (degree)
        {
            case CFDegree.CriticalSuccess:
                _activities.LeapBy(user, 1f);
                _popup.PopupEntity(Loc.GetString("cf-act-high-jump", ("feet", 8)), user, user);
                return true;
            case CFDegree.Success:
                _activities.LeapBy(user, 1f);
                _popup.PopupEntity(Loc.GetString("cf-act-high-jump", ("feet", 5)), user, user);
                return true;
            case CFDegree.CriticalFailure:
                _conditions.KnockProne(user);
                return false;
            default:
                _activities.LeapBy(user, LeapFeet(user) / 5f);
                _popup.PopupEntity(Loc.GetString("cf-act-high-jump-fail"), user, user);
                return false;
        }
    }

    #endregion
}
