using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.GOAP;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Tactics;

/// <summary>
/// Puts numbers on a fight. It rates targets for the sight sensor, and rates actions for the planner, all from what the
/// real rolls would use, so PF2e's math decides. Multiple Attack Penalty does most of the work: a third swing is
/// usually worth less than doing something else with the action.
/// </summary>
public sealed partial class CFGOAPTacticsSystem : EntitySystem
{
    /// <summary>How many Strikes being frightened lasts for, roughly.</summary>
    private const int FrightenedStrikes = 2;

    /// <summary>What off-guard takes from AC, for the Strike after a Feint.</summary>
    private const int OffGuardBonus = 2;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFGOAPSystem _goap = default!;
    [Dependency] private CFManeuverSystem _maneuvers = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;

    private CFGOAPTacticsPrototype TacticsOf(CFGOAPComponent goap)
    {
        return _prototypes.Index(goap.Tactics);
    }

    /// <summary>How much this creature wants to fight that one. Only the order means anything.</summary>
    public float ScoreTarget(Entity<CFGOAPComponent> ent, EntityUid target, float tiles)
    {
        var tactics = TacticsOf(ent.Comp);
        var hitValue = 0f;
        var couldDrop = false;
        if (_strikes.TryPreviewMelee(ent, target, out var strike))
        {
            hitValue = CFGOAPOdds.HitValue(strike.Attack, CFGOAPOdds.JudgedDc(strike.Attack, strike.ArmorClass, tactics.Insight));
            couldDrop = TryComp(target, out CFCombatantComponent? combat) && combat.CurrentHp <= strike.AverageDamage;
        }

        return CFGOAPTargetScore.Score(tactics, new CFGOAPTargetView(
            tiles,
            _goap.Health(target),
            hitValue,
            couldDrop,
            ent.Comp.Target == target,
            ent.Comp.HurtBy == target && _timing.CurTime < ent.Comp.HurtByUntil,
            HasComp<CFSpellbookComponent>(target),
            HasComp<CFCompanionComponent>(target)));
    }

    /// <summary>
    /// What an action is worth right now, where a clean Strike is 1. Zero rules it out. Its cost in the plan is divided
    /// by this, so what the fight makes worthless gets dear.
    /// </summary>
    public float Value(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        var tactics = TacticsOf(ent.Comp);
        switch (action.ValuedBy)
        {
            case CFGOAPValue.Strike:
                return StrikeValue(ent, tactics);
            case CFGOAPValue.Demoralize:
                return DemoralizeValue(ent, tactics);
            case CFGOAPValue.Feint:
                return FeintValue(ent, tactics);
            case CFGOAPValue.HoldBack:
                return tactics.Discipline > 0f ? 1f : 0f;
            default:
                return 1f;
        }
    }

    private bool TryPreview(Entity<CFGOAPComponent> ent, out EntityUid target, out CFStrikePreview strike)
    {
        strike = default;
        target = ent.Comp.Target ?? default;
        return target.IsValid() && !TerminatingOrDeleted(target) && _strikes.TryPreviewMelee(ent, target, out strike);
    }

    private float StrikeValue(Entity<CFGOAPComponent> ent, CFGOAPTacticsPrototype tactics)
    {
        // Something with no stats of its own to weigh, such as a stock mob.
        if (!TryPreview(ent, out _, out var strike))
            return tactics.Strike;

        var dc = CFGOAPOdds.JudgedDc(strike.Attack, strike.ArmorClass, tactics.Insight);
        var share = CFGOAPOdds.StrikeShare(strike.Attack, strike.Map, dc);
        return share < tactics.Discipline ? 0f : share * tactics.Strike;
    }

    private float DemoralizeValue(Entity<CFGOAPComponent> ent, CFGOAPTacticsPrototype tactics)
    {
        if (tactics.Frighten <= 0f
            || !TryPreview(ent, out var target, out var strike)
            || !_maneuvers.CanUse(ent, CFBasicAction.Demoralize, target)
            || !_conditions.TryGetDemoralizeCheck(ent, target, out var skill, out var realDc))
            return 0f;

        // A success is frightened 1 and a critical success is frightened 2, which is just what hit value counts.
        var frightened = CFGOAPOdds.HitValue(skill, CFGOAPOdds.JudgedDc(skill, realDc, tactics.Insight));
        var ac = CFGOAPOdds.JudgedDc(strike.Attack, strike.ArmorClass, tactics.Insight);
        return frightened * CFGOAPOdds.BonusShare(strike.Attack, 1, ac) * FrightenedStrikes * tactics.Frighten;
    }

    private float FeintValue(Entity<CFGOAPComponent> ent, CFGOAPTacticsPrototype tactics)
    {
        if (tactics.Feint <= 0f
            || !TryPreview(ent, out var target, out var strike)
            || !_maneuvers.CanUse(ent, CFBasicAction.Feint, target)
            || !_conditions.TryGetFeintCheck(ent, target, out var skill, out var realDc))
            return 0f;

        var chance = CFGOAPOdds.Chance(skill, CFGOAPOdds.JudgedDc(skill, realDc, tactics.Insight));
        var ac = CFGOAPOdds.JudgedDc(strike.Attack, strike.ArmorClass, tactics.Insight);
        return chance * CFGOAPOdds.BonusShare(strike.Attack, OffGuardBonus, ac) * tactics.Feint;
    }
}
