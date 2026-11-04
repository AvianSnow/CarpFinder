namespace Content.Shared.CF.Checks;

/// <summary>
/// Degree of success of any check: Strike, spell attack, saving throw, skill check, or flat check.
/// </summary>
public enum CFDegree : byte
{
    CriticalFailure,
    Failure,
    Success,
    CriticalSuccess,
}

/// <summary>
/// Remaster degree-of-success math. Beat the DC by 10 for a critical success, miss it by 10 for a
/// critical failure. A natural 20 improves the result one step and a natural 1 worsens it one step.
/// </summary>
public static class CFDegrees
{
    public static CFDegree DegreeOfSuccess(int d20, int modifier, int dc)
    {
        var total = d20 + modifier;
        var degree = total >= dc ? CFDegree.Success : CFDegree.Failure;

        if (total >= dc + 10)
            degree = CFDegree.CriticalSuccess;
        else if (total <= dc - 10)
            degree = CFDegree.CriticalFailure;

        if (d20 == 20)
            degree = Improve(degree);
        else if (d20 == 1)
            degree = Worsen(degree);

        return degree;
    }

    public static bool IsSuccess(this CFDegree degree)
    {
        return degree is CFDegree.Success or CFDegree.CriticalSuccess;
    }

    public static CFDegree Improve(CFDegree degree)
    {
        return degree switch
        {
            CFDegree.CriticalFailure => CFDegree.Failure,
            CFDegree.Failure => CFDegree.Success,
            _ => CFDegree.CriticalSuccess,
        };
    }

    public static CFDegree Worsen(CFDegree degree)
    {
        return degree switch
        {
            CFDegree.CriticalSuccess => CFDegree.Success,
            CFDegree.Success => CFDegree.Failure,
            _ => CFDegree.CriticalFailure,
        };
    }
}
