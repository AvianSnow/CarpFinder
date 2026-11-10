namespace Content.Server.CF.GOAP.Sensors;

/// <summary>
/// A creature that keeps a lair, such as a dragon. Its lair is its home. It waits there for what comes in, only goes for
/// an enemy that comes close, and goes back once it strays too far or has been away too long.
/// <see cref="CFGOAPLairSensorSystem"/> reads this.
/// </summary>
[RegisterComponent]
public sealed partial class CFGOAPLairComponent : Component
{
    /// <summary>It goes for an enemy within this many tiles of itself, and waits for anything further away.</summary>
    [DataField]
    public float EngageTiles = 3f;

    /// <summary>Further than this from home, in tiles, it goes back.</summary>
    [DataField]
    public float LeashTiles = 8f;

    /// <summary>Away from home for this long, it goes back, however close it still is.</summary>
    [DataField]
    public TimeSpan ChaseTime = TimeSpan.FromSeconds(10);

    /// <summary>When it last left home.</summary>
    [ViewVariables]
    public TimeSpan? AwaySince;

    /// <summary>It has set off back, and keeps going until it is home.</summary>
    [ViewVariables]
    public bool Returning;
}
