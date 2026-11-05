using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared.CF.Detection;

/// <summary>What a creature knows about another one. Each observer has its own.</summary>
public enum CFDetection : byte
{
    /// <summary>In clear view.</summary>
    Observed,

    /// <summary>It knows the square but cannot see the creature.</summary>
    Hidden,

    /// <summary>It does not know where the creature is.</summary>
    Undetected,
}

/// <summary>A creature trying not to be seen. Added the first time it Hides.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFStealthComponent : Component
{
    /// <summary>Whether it is hiding at all. Off, every creature observes it.</summary>
    [AutoNetworkedField]
    public bool Active;

    /// <summary>
    /// How each creature it has checked against sees it. An observer that failed stays here as Observed,
    /// so only a creature that was not around yet gets a new roll.
    /// </summary>
    [AutoNetworkedField]
    public Dictionary<EntityUid, CFDetection> Detection = new();

    /// <summary>Who has seen a diversion from this creature lately. They are harder to fool again.</summary>
    public Dictionary<EntityUid, TimeSpan> DivertedUntil = new();

    /// <summary>Seconds of walking since the last Sneak.</summary>
    public float SneakProgress;

    /// <summary>A shot gives the creature away after it lands, so the shot itself counts as unseen.</summary>
    public TimeSpan? RevealAt;

    /// <summary>Client-only. The alpha last drawn, so nothing is written while it does not change.</summary>
    public float ClientAlpha = 1f;
}

/// <summary>Raised on a creature that points at the ground, where stock raises nothing.</summary>
[ByRefEvent]
public readonly record struct CFPointedAtGroundEvent(MapCoordinates Coordinates);
