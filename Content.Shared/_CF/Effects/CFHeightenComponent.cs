namespace Content.Shared.CF.Effects;

/// <summary>
/// Heightening schedule for a spell. Each <see cref="Interval"/> ranks past <see cref="BaseRank"/> is one step.
/// Damage and heal effects add their own <c>heightenDice</c> and <c>heightenFlat</c> per step.
/// </summary>
/// <example>
/// A cantrip that adds 1d4 "at 3rd rank and every 2 ranks after" is <c>baseRank: 1, interval: 2</c>
/// with <c>heightenDice: 1</c> on its damage. Fireball, "+2d6 per rank above 3rd", is <c>baseRank: 3</c>
/// with <c>heightenDice: 2</c>.
/// </example>
[RegisterComponent]
public sealed partial class CFHeightenComponent : Component
{
    [DataField]
    public int BaseRank = 1;

    /// <summary>1 is heightened (+1). 2 is heightened (+2).</summary>
    [DataField]
    public int Interval = 1;

    /// <summary>Extra effects for a landed check from a given rank on, such as "heightened (4th)".</summary>
    [DataField]
    public Dictionary<int, List<CFEffect>> AtRank = new();

    public int Steps(int rank)
    {
        if (rank <= BaseRank)
            return 0;

        return (rank - BaseRank) / Math.Max(1, Interval);
    }
}
