using Robust.Shared.GameStates;

namespace Content.Shared.CF.Time;

/// <summary>
/// Which clock a duration belongs to. These are not four names for one multiplier.
/// Real and Adjusted are wall-clock seconds the engine waits.
/// Pathfinder is the tabletop book and is not a timer.
/// CarpFinder is the world clock: day, night, and the round.
/// </summary>
public enum CFTimeScale : byte
{
    /// <summary>Wall clock. One second is one second of game time.</summary>
    Real = 0,

    /// <summary>Tabletop Pathfinder. One round is <see cref="CFTimeComponent.PathfinderRoundSeconds"/>.</summary>
    Pathfinder = 1,

    /// <summary>Wall-clock seconds chosen for this feature, instead of the book duration.</summary>
    Adjusted = 2,

    /// <summary>World clock. Day, night, and the length of a round.</summary>
    CarpFinder = 3,
}

/// <summary>
/// Rates for the four CarpFinder clocks. The numbers live on <c>CFWorldClock</c>.
/// A round starts at dawn. Day runs first, then night, and together they are one round.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFTimeComponent : Component
{
    /// <summary>World seconds that pass during one real second. 4 makes a 6-hour round into one day.</summary>
    [DataField, AutoNetworkedField]
    public float InGameSecondsPerRealSecond = 4f;

    /// <summary>How long one round lasts on the wall clock, in hours.</summary>
    [DataField, AutoNetworkedField]
    public float RoundRealHours = 6f;

    /// <summary>Daylight at the start of the round, in world hours.</summary>
    [DataField, AutoNetworkedField]
    public float DayInGameHours = 14f;

    /// <summary>Night after the day, in world hours.</summary>
    [DataField, AutoNetworkedField]
    public float NightInGameHours = 10f;

    /// <summary>One Pathfinder round, in book seconds. Combat does not tick in these rounds.</summary>
    [DataField, AutoNetworkedField]
    public float PathfinderRoundSeconds = 6f;

    public float RealSecondsPerInGameSecond =>
        InGameSecondsPerRealSecond <= 0f ? 0f : 1f / InGameSecondsPerRealSecond;

    public float RoundInGameHours => RoundRealHours * InGameSecondsPerRealSecond;

    public float DayRealHours =>
        InGameSecondsPerRealSecond <= 0f ? 0f : DayInGameHours / InGameSecondsPerRealSecond;

    public float NightRealHours =>
        InGameSecondsPerRealSecond <= 0f ? 0f : NightInGameHours / InGameSecondsPerRealSecond;

    /// <summary>
    /// Real and Adjusted are already wall-clock seconds.
    /// CarpFinder converts world seconds into wall-clock seconds.
    /// Pathfinder does not convert: pick an Adjusted duration for that feature.
    /// </summary>
    public bool TryGetRealSeconds(float amount, CFTimeScale scale, out float realSeconds)
    {
        switch (scale)
        {
            case CFTimeScale.Real:
            case CFTimeScale.Adjusted:
                realSeconds = amount;
                return true;
            case CFTimeScale.CarpFinder:
                realSeconds = RealSecondsPerInGameSecond * amount;
                return InGameSecondsPerRealSecond > 0f;
            default:
                realSeconds = 0f;
                return false;
        }
    }

    public float ToCarpFinderSeconds(float realSeconds)
    {
        return realSeconds * InGameSecondsPerRealSecond;
    }

    public float PathfinderSeconds(float rounds)
    {
        return rounds * PathfinderRoundSeconds;
    }

    public float PathfinderRounds(float pathfinderSeconds)
    {
        return PathfinderRoundSeconds <= 0f ? 0f : pathfinderSeconds / PathfinderRoundSeconds;
    }

    /// <summary>True during the daylight portion. <paramref name="realSinceDawn"/> is wall-clock time since the round started.</summary>
    public bool IsDay(TimeSpan realSinceDawn)
    {
        var day = DayRealHours * 3600d;
        var cycle = day + NightRealHours * 3600d;
        if (cycle <= 0d)
            return true;

        var elapsed = realSinceDawn.TotalSeconds % cycle;
        if (elapsed < 0d)
            elapsed += cycle;

        return elapsed < day;
    }
}
