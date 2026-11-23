namespace Content.Shared.CF.ZLevels;

/// <summary>The Player Core falling rules.</summary>
public static class CFFallRules
{
    public const int MaxFeet = 1500;

    /// <summary>How far apart two levels are. One level of vertical physics is this many feet.</summary>
    public const int LevelFeet = 10;

    /// <summary>Half the distance, for any fall over 5 feet. Falls past 1,500 feet count as 1,500.</summary>
    public static int Damage(int feet)
    {
        return feet <= 5 ? 0 : Math.Min(feet, MaxFeet) / 2;
    }

    /// <summary>
    /// The distance a creature fell, from the speed it landed at. Vertical physics measures height in levels and
    /// accelerates at <paramref name="gravity"/> levels per second squared, so the height is v² / 2g levels.
    /// Rounded to the nearest 5 feet, the grid the rules count in.
    /// </summary>
    public static int FeetFromImpact(float impactSpeed, float gravity)
    {
        if (gravity <= 0 || impactSpeed <= 0)
            return 0;

        var levels = impactSpeed * impactSpeed / (2 * gravity);
        return (int)Math.Round(levels * LevelFeet / 5) * 5;
    }
}
