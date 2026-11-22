namespace Content.Shared.CF.Spells;

/// <summary>The one-word orders of the Command spell. The values are the spell's mode values.</summary>
public enum CFCommandOrder : byte
{
    None = 0,

    /// <summary>Walk to the caster.</summary>
    Approach = 1,

    /// <summary>Let go of everything held.</summary>
    Drop = 2,

    /// <summary>Fall prone.</summary>
    Fall = 3,

    /// <summary>Run from the caster.</summary>
    Flee = 4,

    /// <summary>Stand still.</summary>
    Halt = 5,
}

/// <summary>
/// A creature obeying a movement order from Command. The server walks it: a GOAP brain plans <c>Obey</c>, and a stock
/// HTN mob has its HTN paused and is steered by GOAP steering until <see cref="Until"/>.
/// Drop and Fall happen at once and never add this.
/// </summary>
[RegisterComponent]
public sealed partial class CFCommandedComponent : Component
{
    [DataField]
    public CFCommandOrder Order;

    /// <summary>Who gave the order: the creature to approach or run from.</summary>
    [DataField]
    public EntityUid Commander;

    [DataField]
    public TimeSpan Until;

    /// <summary>It has reached the caster or been told to halt, and is holding still.</summary>
    [DataField]
    public bool Holding;

    /// <summary>Its stock HTN was switched off for this order, and must be switched back on.</summary>
    [DataField]
    public bool PausedHtn;
}
