namespace Content.Shared.CF.Checks;

/// <summary>
/// The defense a check is rolled against. Not tied to spells: skill actions, items, and hazards use it too.
/// </summary>
public enum CFDefenseType : byte
{
    None,
    ArmorClass,
    Fortitude,
    Reflex,
    Will,
    Perception,
}

public static class CFDefenseTypes
{
    public static bool IsSave(this CFDefenseType defense)
    {
        return defense is CFDefenseType.Fortitude or CFDefenseType.Reflex or CFDefenseType.Will;
    }
}
