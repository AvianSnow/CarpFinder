using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Content.Shared.CF.ZLevels.Ladder;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server.CF.ZLevels.Ladder;

/// <summary>
/// The Player Core Climb check on CrystallEdge's ladders. <c>CFLadderSystem</c> asks with
/// <see cref="CFLadderGripAttemptEvent"/> as a creature touches a ladder, and this answers from the ladder's
/// <see cref="CFLadderCheckComponent"/>. Only creatures with a stat block roll: stock mobs climb as CrystallEdge lets them.
/// </summary>
public sealed partial class CFLadderCheckSystem : EntitySystem
{
    /// <summary>Farther than this above the floor, the creature is coming down from the level above.</summary>
    private const float GroundTolerance = 0.1f;

    /// <summary>How long a refusal that was not a roll holds: full hands, or no action to spend.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(1);

    [Dependency] private CFActionPoolSystem _actionPool = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFSharedZLevelsSystem _zLevels = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [SubscribeLocalEvent]
    private void OnGripAttempt(Entity<CFLadderCheckComponent> ent, ref CFLadderGripAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryGrip(ent, args.Climber))
            args.Cancelled = true;
    }

    /// <summary>
    /// Whether the creature may take hold of the ladder now. A verdict holds for the ladder's <c>RetrySeconds</c>,
    /// so a creature that keeps touching it does not roll or pay again until then.
    /// </summary>
    public bool TryGrip(Entity<CFLadderCheckComponent> ladder, EntityUid climber)
    {
        var now = _timing.CurTime;
        Prune(ladder.Comp, now);

        if (ladder.Comp.Verdicts.TryGetValue(climber, out var verdict))
            return verdict.Allowed;

        var allowed = Judge(ladder, climber, out var hold);
        if (hold > TimeSpan.Zero)
            ladder.Comp.Verdicts[climber] = (now + hold, allowed);

        return allowed;
    }

    private bool Judge(Entity<CFLadderCheckComponent> ladder, EntityUid climber, out TimeSpan hold)
    {
        hold = TimeSpan.Zero;
        if (ladder.Comp.Dc <= 0 || !HasComp<CFCombatantComponent>(climber))
            return true;

        var options = CompOrNull<CFCombatOptionsComponent>(climber);
        if (options is { ClimbSpeed: true })
            return true;

        if (ladder.Comp.NeedsFreeHands)
        {
            foreach (var _ in _hands.EnumerateHeld(climber))
            {
                _popup.PopupEntity(Loc.GetString("cf-zlevel-hands-full"), ladder, climber);
                hold = Pause;
                return false;
            }
        }

        if (!_actionPool.TrySpendActions(climber, ladder.Comp.Actions))
        {
            hold = Pause;
            return false;
        }

        // Off the floor already: it stepped into the opening above and is coming down, so only letting go matters.
        var descending = _zLevels.DistanceToGround(climber) > GroundTolerance;

        var degree = _conditions.RollSkillAgainst(climber, "Athletics", CFAbility.Strength, ladder.Comp.Dc, out _, itemBonus: options?.ClimbBonus ?? 0);
        if (degree == CFDegree.Success && options is { ClimbSuccessCritical: true })
            degree = CFDegree.CriticalSuccess;

        hold = TimeSpan.FromSeconds(ladder.Comp.RetrySeconds);
        switch (degree)
        {
            case CFDegree.CriticalFailure:
                if (!descending)
                    _conditions.KnockProne(climber);

                _popup.PopupEntity(Loc.GetString("cf-zlevel-climb-failed"), ladder, climber);
                return false;
            case CFDegree.Failure:
                if (descending)
                    return true;

                _popup.PopupEntity(Loc.GetString("cf-zlevel-climb-failed"), ladder, climber);
                return false;
            default:
                return true;
        }
    }

    private static void Prune(CFLadderCheckComponent check, TimeSpan now)
    {
        if (check.Verdicts.Count == 0)
            return;

        var expired = new List<EntityUid>();
        foreach (var (uid, verdict) in check.Verdicts)
        {
            if (now >= verdict.Until)
                expired.Add(uid);
        }

        foreach (var uid in expired)
        {
            check.Verdicts.Remove(uid);
        }
    }
}
