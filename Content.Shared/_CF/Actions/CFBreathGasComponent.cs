using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Actions;

/// <summary>
/// The breath comes out as a cloud of gas, such as a dragon's toxic breath. It goes on the breath action beside
/// <see cref="CFBreathWeaponComponent"/>. The gas is released from the breather and shared between neighbouring tiles
/// the way SS14 atmos shares it, but only over the tiles of the breath's area that it can reach, so it fills the cone
/// and stops at walls. A creature standing on a tile once it is filled rolls against the breath, once per breath,
/// whether it was there when it filled or walked in later. The cloud lingers, then thins out and is gone.
/// <see cref="CFBreathGasSystem"/>. Gas is measured so that 1 is a tile of the settled cloud.
/// </summary>
[RegisterComponent]
public sealed partial class CFBreathGasComponent : Component
{
    /// <summary>What each tile of cloud looks like: an entity with <see cref="CFBreathCloudTileComponent"/>.</summary>
    [DataField]
    public EntProtoId Cloud = "CFBreathCloudPoison";

    /// <summary>How long the breather keeps letting gas out.</summary>
    [DataField]
    public float EmitSeconds = 1.5f;

    /// <summary>
    /// How often a tile shares a fifth of the difference with each neighbour. Higher spreads faster. 20 fills a 30-foot
    /// cone in about 2 seconds.
    /// </summary>
    [DataField]
    public float SpreadsPerSecond = 20f;

    /// <summary>How thick a tile's gas has to be before standing in it means rolling.</summary>
    [DataField]
    public float Filled = 0.5f;

    /// <summary>Seconds after the breath before the cloud starts to thin.</summary>
    [DataField]
    public float LingerSeconds = 4f;

    /// <summary>The share of each tile's gas lost per second once it thins.</summary>
    [DataField]
    public float FadePerSecond = 0.5f;
}

/// <summary>A gas breath in the air, on an entity of its own in nullspace. Server only. Removed when the cloud is gone.</summary>
[RegisterComponent]
public sealed partial class CFBreathCloudComponent : Component
{
    /// <summary>The breath action: its area, check, effects and <see cref="CFBreathGasComponent"/>.</summary>
    [DataField]
    public EntityUid Source;

    [DataField]
    public EntityUid User;

    [DataField]
    public EntityUid Grid;

    [DataField]
    public EntProtoId Cloud;

    [DataField]
    public float EmitSeconds;

    [DataField]
    public float SpreadsPerSecond;

    [DataField]
    public float Filled;

    [DataField]
    public float LingerSeconds;

    [DataField]
    public float FadePerSecond;

    [DataField]
    public TimeSpan Start;

    [DataField]
    public TimeSpan NextStep;

    /// <summary>The tiles it can spread over.</summary>
    [DataField]
    public List<Vector2i> Tiles = new();

    /// <summary>How much gas is on each of <see cref="Tiles"/>.</summary>
    [DataField]
    public List<float> Gas = new();

    /// <summary>The thickness each tile shows, 0 to <see cref="CFBreathGasSystem.Levels"/>.</summary>
    [DataField]
    public List<int> Shown = new();

    /// <summary>The cloud drawn on each tile, spawned when gas first reaches it.</summary>
    [DataField]
    public List<EntityUid?> Clouds = new();

    /// <summary>Indexes into <see cref="Tiles"/> that the gas comes out on: those beside the breather.</summary>
    [DataField]
    public List<int> Mouth = new();

    /// <summary>Creatures that have rolled against this breath. Nobody rolls twice.</summary>
    [DataField]
    public HashSet<EntityUid> Caught = new();
}

/// <summary>One tile of a breath's cloud. Only drawn; it does nothing by itself.</summary>
[RegisterComponent]
public sealed partial class CFBreathCloudTileComponent : Component
{
}

[Serializable, NetSerializable]
public enum CFBreathCloudVisuals : byte
{
    /// <summary>An int, 0 (none) to <see cref="CFBreathGasSystem.Levels"/> (thickest).</summary>
    Density,
}
