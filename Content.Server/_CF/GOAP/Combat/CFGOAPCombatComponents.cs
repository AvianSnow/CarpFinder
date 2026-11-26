namespace Content.Server.CF.GOAP.Combat;

/// <summary>
/// Swinging at <see cref="Target"/> whenever it is in reach with a clear line. A step puts this on the attacker and
/// takes it off when it ends; <see cref="CFGOAPCombatSystem"/> does the swinging.
/// </summary>
[RegisterComponent]
public sealed partial class CFGOAPMeleeComponent : Component
{
    [ViewVariables]
    public EntityUid Target;
}

/// <summary>Shooting <see cref="Target"/> with the gun or bow in hand while the line to it is clear.</summary>
[RegisterComponent]
public sealed partial class CFGOAPRangedComponent : Component
{
    [ViewVariables]
    public EntityUid Target;

    /// <summary>Seconds until the line to the target is checked again. A ray each tick would be most of the cost.</summary>
    public float LineAccumulator;

    /// <summary>What the last check found.</summary>
    public bool TargetInLine;

    /// <summary>How long the target has been in a clear line, in seconds. It takes a moment to aim before the first shot.</summary>
    public float ShootAccumulator;
}
