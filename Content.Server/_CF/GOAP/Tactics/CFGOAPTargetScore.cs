using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Tactics;

/// <summary>What a creature can tell about one possible target.</summary>
/// <param name="Tiles">How far it would have to go.</param>
/// <param name="Health">Its share of hit points, from 0 to 1.</param>
/// <param name="HitValue">What a Strike at it looks worth. See <see cref="CFGOAPOdds.HitValue"/>.</param>
/// <param name="CouldDrop">One plain hit could drop it.</param>
/// <param name="IsCurrent">It is already the target.</param>
/// <param name="HurtMe">It hurt this creature a moment ago.</param>
/// <param name="IsCaster">It casts spells.</param>
/// <param name="IsCompanion">It is someone's companion.</param>
public readonly record struct CFGOAPTargetView(
    float Tiles,
    float Health,
    float HitValue,
    bool CouldDrop,
    bool IsCurrent,
    bool HurtMe,
    bool IsCaster,
    bool IsCompanion);

/// <summary>Scoring a target as plain math, so it tests without a game.</summary>
public static class CFGOAPTargetScore
{
    public static float Score(CFGOAPTacticsPrototype tactics, CFGOAPTargetView target)
    {
        var score = target.HitValue - tactics.Distance * target.Tiles + tactics.Wounded * (1f - target.Health);
        if (target.CouldDrop)
            score += tactics.Finish;
        if (target.IsCurrent)
            score += tactics.Commitment;
        if (target.HurtMe)
            score += tactics.Payback;
        if (target.IsCaster)
            score += tactics.Casters;
        if (target.IsCompanion)
            score += tactics.Companions;

        return score;
    }
}
