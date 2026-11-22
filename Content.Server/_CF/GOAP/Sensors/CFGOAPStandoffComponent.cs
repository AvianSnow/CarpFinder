namespace Content.Server.CF.GOAP.Sensors;

/// <summary>
/// A creature that fights at range and carries a melee weapon put away. It backs off from what comes close, and draws
/// the weapon when it is cornered or out of ammunition. <see cref="CFGOAPStandoffSensorSystem"/> reads this.
/// </summary>
[RegisterComponent]
public sealed partial class CFGOAPStandoffComponent : Component
{
    /// <summary>The inventory slot the melee weapon is in.</summary>
    [DataField]
    public string SidearmSlot = "pocket1";

    /// <summary>An enemy this near makes it back off, in tiles.</summary>
    [DataField]
    public float KeepTiles = 3f;

    /// <summary>Once backing off, it keeps going until the enemy is this far, in tiles, so it does not stop and start.</summary>
    [DataField]
    public float ClearTiles = 5f;

    /// <summary>An enemy staying in reach for this long has caught it, which is as good as a wall behind it.</summary>
    [DataField]
    public TimeSpan CaughtTime = TimeSpan.FromSeconds(3);

    /// <summary>When an enemy came into reach, while it still has the ranged weapon out.</summary>
    [ViewVariables]
    public TimeSpan? InReachSince;
}
