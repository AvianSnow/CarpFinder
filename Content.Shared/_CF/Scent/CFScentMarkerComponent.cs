// Ported from Starlight (https://github.com/ss14Starlight/space-station-14), their Scent system, with permission.

using Robust.Shared.GameStates;

namespace Content.Shared.CF.Scent;

/// <summary>
/// One mark of a scent trail: a short-lived entity on the floor that only a sniffing <see cref="CFSmellerComponent"/> sees.
/// <c>TimedDespawn</c> removes it. See <c>CFScentMarker</c> in <c>Entities/Effects/scent.yml</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class CFScentMarkerComponent : Component
{
    [DataField, AutoNetworkedField]
    public string ScentId = string.Empty;

    /// <summary>When the mark is gone. Absolute, so the fade can be worked out again whenever it restarts.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt;

    /// <summary>How pooled the mark is, 0 to 1. Drawn as alpha and size.</summary>
    [DataField, AutoNetworkedField]
    public float Strength;

    /// <summary>The whole life of the mark. <see cref="ExpiresAt"/> less this is when it started.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan TotalDuration;
}
