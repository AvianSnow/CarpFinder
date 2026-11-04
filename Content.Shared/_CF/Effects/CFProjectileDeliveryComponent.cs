using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>
/// The source fires bolts that home on the clicked creature and resolve on arrival.
/// With <see cref="MaxBolts"/> above 1 the user picks a count and clicks once per bolt (Force Barrage).
/// </summary>
[RegisterComponent]
public sealed partial class CFProjectileDeliveryComponent : Component
{
    [DataField]
    public EntProtoId BoltPrototype = "CFSpellBolt";

    [DataField]
    public float Speed = 18f;

    /// <summary>
    /// Highest number of bolts one use can fire. 1 is a single shot.
    /// Above 1, each bolt costs one action and the source (spell slot) is paid once.
    /// </summary>
    [DataField]
    public int MaxBolts = 1;

    /// <summary>Bolts chosen for the volley being aimed. The server copies this from the perform request.</summary>
    [DataField]
    public int ChosenBolts = 1;

    /// <summary>Bolts of this volley that still need a click. Counted down on the client as you aim.</summary>
    public int VolleyLeft;

    /// <summary>The first bolt of this volley has been sent. Later clicks do not pay again.</summary>
    public bool VolleyArmed;

    /// <summary>This perform is the opening shot, so it pays for the whole volley.</summary>
    public bool PayThisShot;

    public bool IsVolley => MaxBolts > 1;

    /// <summary>The chosen count clamped to 1..<see cref="MaxBolts"/>.</summary>
    public int ClampedBolts => Math.Clamp(ChosenBolts < 1 ? 1 : ChosenBolts, 1, Math.Max(1, MaxBolts));
}
