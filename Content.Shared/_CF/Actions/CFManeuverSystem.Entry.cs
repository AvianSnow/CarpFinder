using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.Actions;

/// <summary>Actions with no wheel entry, for anything that has no click to make.</summary>
public enum CFBasicAction : byte
{
    Demoralize,
    Feint,
    Hide,
    Sneak,
    Seek,
    TumbleThrough,
    Leap,
    Reposition,
    Aid,
    RecallKnowledge,

    /// <summary>Shove or Disarm, whichever a right-click on this target would do.</summary>
    Maneuver,
    Grapple,
}

/// <summary>An action button for a basic action. It pays through <see cref="CFManeuverSystem.TryUse"/>.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFBasicActionComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public CFBasicAction Action;
}

/// <summary>
/// One way in to each action for code with no player behind it. The AI planner asks <see cref="CanUse"/> as a
/// precondition and calls <see cref="TryUse"/> to act, so it follows the same rules as stock inputs.
/// </summary>
public sealed partial class CFManeuverSystem
{
    /// <summary>Whether the action could work now. Nothing is spent or rolled.</summary>
    public bool CanUse(EntityUid user, CFBasicAction action, EntityUid? target = null)
    {
        if (!HasStats(user) || !_actions.CanAffordActions(user, 1))
            return false;

        switch (action)
        {
            case CFBasicAction.Hide:
                return true;
            case CFBasicAction.Sneak:
                return TryComp(user, out CFStealthComponent? stealth) && stealth.Active;
            case CFBasicAction.Seek:
                return !_conditions.IsProne(user);
            case CFBasicAction.Leap:
                return !_conditions.IsProne(user) && CFActivitySystem.LeapTiles(Comp<CFCombatantComponent>(user).SpeedFeet) > 0;
        }

        if (target is not { } other)
            return false;

        return action switch
        {
            CFBasicAction.Demoralize => IsEnemy(user, other)
                                        && _conditions.CanDemoralize(user, other)
                                        && _interaction.InRangeUnobstructed(user, other, DemoralizeFeet / CFAreaGrid.FeetPerTile + 1f),
            CFBasicAction.Feint => IsEnemy(user, other) && CanFeint(user, other),
            CFBasicAction.TumbleThrough => IsEnemy(user, other) && InReach(user, other, CFFlanking.DefaultReachFeet),
            CFBasicAction.Reposition => IsEnemy(user, other)
                                        && InReach(user, other, CFFlanking.DefaultReachFeet)
                                        && (_hands.CountFreeHands(user) > 0 || _conditions.IsGrabbing(user, other)),
            CFBasicAction.Aid => !_factions.CanHarm(user, other) && InReach(user, other, CFFlanking.DefaultReachFeet),
            CFBasicAction.RecallKnowledge => _conditions.CanRecall(user, other),
            CFBasicAction.Maneuver => IsEnemy(user, other)
                                      && InReach(user, other, CFFlanking.DefaultReachFeet)
                                      && Decide(user, other) != CFManeuver.None,
            CFBasicAction.Grapple => IsEnemy(user, other)
                                     && InReach(user, other, CFFlanking.DefaultReachFeet)
                                     && _hands.CountFreeHands(user) > 0,
            _ => false,
        };
    }

    /// <summary>Does the action, if <see cref="CanUse"/> allows it. Seek looks around the target, or the user.</summary>
    public bool TryUse(EntityUid user, CFBasicAction action, EntityUid? target = null)
    {
        if (!_net.IsServer || !CanUse(user, action, target))
            return false;

        switch (action)
        {
            case CFBasicAction.Hide:
                return _stealth.TryHide(user);
            case CFBasicAction.Sneak:
                return _stealth.TrySneak(user);
            case CFBasicAction.Seek:
                return _stealth.TrySeek(user, _transform.GetMapCoordinates(target ?? user));
            case CFBasicAction.Leap:
                if (!_actions.TrySpendActions(user, 1))
                    return false;

                _activities.Leap(user);
                return true;
        }

        var other = target!.Value;
        return action switch
        {
            CFBasicAction.Demoralize => _actions.TrySpendActions(user, 1) && _conditions.TryDemoralize(user, other),
            CFBasicAction.Feint => _actions.TrySpendActions(user, 1) && _conditions.TryFeint(user, other),
            CFBasicAction.TumbleThrough => TryTumble(user, other),
            CFBasicAction.Reposition => TryReposition(user, other),
            CFBasicAction.Aid => TryAid(user, other),
            CFBasicAction.RecallKnowledge => TryRecall(user, other)?.IsSuccess() ?? false,
            CFBasicAction.Maneuver => TryManeuver(user, other) != CFManeuver.None,
            CFBasicAction.Grapple => _pulling.TryStartPull(user, other),
            _ => false,
        };
    }

    [SubscribeLocalEvent]
    private void OnBasicInstant(Entity<CFBasicActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        PerformBasic(args.Performer, ent.Comp.Action, null);
    }

    [SubscribeLocalEvent]
    private void OnBasicTarget(Entity<CFBasicActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        PerformBasic(args.Performer, ent.Comp.Action, args.Target);
    }

    private void PerformBasic(EntityUid user, CFBasicAction action, EntityUid? target)
    {
        if (!_net.IsServer)
            return;

        // Asked first, because TryUse also returns false for a roll that failed.
        if (!CanUse(user, action, target))
        {
            _popup.PopupEntity(Loc.GetString("cf-act-cannot"), user, user);
            return;
        }

        TryUse(user, action, target);
    }

    private bool IsEnemy(EntityUid user, EntityUid target)
    {
        return _mobState.IsAlive(target) && _factions.CanHarm(user, target);
    }

    private bool InReach(EntityUid user, EntityUid target, int feet)
    {
        return CFFlanking.InReach(_transform.GetWorldPosition(user), _transform.GetWorldPosition(target), feet);
    }
}
