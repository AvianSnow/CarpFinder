namespace Content.Shared.CF.Character;

/// <summary>
/// Pathfinder medium-advancement awards. The amount is per character.
/// A larger or smaller party still uses these numbers.
/// </summary>
public static class CFExperienceAwards
{
    public static bool TryAccomplishment(string name, out int xp)
    {
        switch (name.ToLowerInvariant())
        {
            case "minor":
                xp = 10;
                return true;
            case "moderate":
                xp = 30;
                return true;
            case "major":
                xp = 80;
                return true;
            default:
                xp = 0;
                return false;
        }
    }

    /// <summary>Creature XP by how far its level sits from the party level. Range is -4 through +4.</summary>
    public static bool TryAdversary(int relativeLevel, out int xp)
    {
        xp = relativeLevel switch
        {
            -4 => 10,
            -3 => 15,
            -2 => 20,
            -1 => 30,
            0 => 40,
            1 => 60,
            2 => 80,
            3 => 120,
            4 => 160,
            _ => 0,
        };
        return relativeLevel is >= -4 and <= 4;
    }

    /// <summary>Hazard XP for the same relative levels. A complex hazard matches a creature. A simple hazard is lower.</summary>
    public static bool TryHazard(bool complex, int relativeLevel, out int xp)
    {
        if (complex)
            return TryAdversary(relativeLevel, out xp);

        xp = relativeLevel switch
        {
            -4 => 2,
            -3 => 3,
            -2 => 4,
            -1 => 6,
            0 => 8,
            1 => 12,
            2 => 16,
            3 => 24,
            4 => 32,
            _ => 0,
        };
        return relativeLevel is >= -4 and <= 4;
    }
}
