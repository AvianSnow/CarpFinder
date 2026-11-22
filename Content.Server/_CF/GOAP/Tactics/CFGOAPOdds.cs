using Content.Shared.CF.Checks;

namespace Content.Server.CF.GOAP.Tactics;

/// <summary>What a check is likely to do, worked out from the real degree-of-success rules. Plain math, so it tests without a game.</summary>
public static class CFGOAPOdds
{
    private const int DieFaces = 20;

    /// <summary>The DC someone assumes against an even opponent: a 10 or better on the die.</summary>
    private const int EvenMargin = 10;

    /// <summary>The chance of a success or better.</summary>
    public static float Chance(int modifier, int dc)
    {
        var successes = 0;
        for (var d20 = 1; d20 <= DieFaces; d20++)
        {
            if (CFDegrees.DegreeOfSuccess(d20, modifier, dc).IsSuccess())
                successes++;
        }

        return (float) successes / DieFaces;
    }

    /// <summary>What a roll is worth on average, where a success counts 1 and a critical success counts 2.</summary>
    public static float HitValue(int modifier, int dc)
    {
        var total = 0;
        for (var d20 = 1; d20 <= DieFaces; d20++)
        {
            total += CFDegrees.DegreeOfSuccess(d20, modifier, dc) switch
            {
                CFDegree.CriticalSuccess => 2,
                CFDegree.Success => 1,
                _ => 0,
            };
        }

        return (float) total / DieFaces;
    }

    /// <summary>
    /// The DC it thinks it faces. <paramref name="insight"/> blends from an even fight at 0 to the real one at 1.
    /// </summary>
    public static int JudgedDc(int modifier, int realDc, float insight)
    {
        var even = modifier + EvenMargin;
        return (int) MathF.Round(even + (realDc - even) * Math.Clamp(insight, 0f, 1f));
    }

    /// <summary>
    /// A Strike after <paramref name="map"/>, as a share of the same Strike with none. A target it could never hit
    /// loses nothing to the penalty, so that counts as 1: this is about the penalty, not about whether to fight at all.
    /// </summary>
    public static float StrikeShare(int attack, int map, int dc)
    {
        var clean = HitValue(attack, dc);
        return clean <= 0f ? 1f : HitValue(attack + map, dc) / clean;
    }

    /// <summary>What a <paramref name="bonus"/> adds to one Strike, as a share of that Strike.</summary>
    public static float BonusShare(int attack, int bonus, int dc)
    {
        var clean = HitValue(attack, dc);
        return clean <= 0f ? 0f : (HitValue(attack + bonus, dc) - clean) / clean;
    }
}
