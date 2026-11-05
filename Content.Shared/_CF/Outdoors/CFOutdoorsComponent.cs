using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Outdoors;

/// <summary>GM Core temperature categories.</summary>
public enum CFTemperatureBand : byte
{
    IncredibleCold,
    ExtremeCold,
    SevereCold,
    MildCold,
    Normal,
    MildHeat,
    SevereHeat,
    ExtremeHeat,
    IncredibleHeat,
}

/// <summary>
/// Makes a map an outdoor world: day and night from the world clock, a temperature, and changing weather.
/// Anything roofed or on a tile without weather counts as indoors.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class CFOutdoorsComponent : Component
{
    [DataField, AutoNetworkedField]
    public CFTemperatureBand Temperature = CFTemperatureBand.Normal;

    /// <summary>What the sky can do next, picked by weight. Empty means the weather never changes by itself.</summary>
    [DataField]
    public List<CFWeatherEntry> Weather = new();

    [DataField, AutoPausedField]
    public TimeSpan NextWeather;

    [DataField, AutoPausedField]
    public TimeSpan NextExposure;

    /// <summary>Exposure ticks so far. Protected creatures are hurt on only some of them.</summary>
    [DataField]
    public int ExposureTicks;
}

/// <summary>One possible weather spell. No <see cref="Weather"/> means a clear sky for that long.</summary>
[DataDefinition]
public sealed partial class CFWeatherEntry
{
    [DataField]
    public EntProtoId? Weather;

    [DataField]
    public float Weight = 1f;

    /// <summary>Shortest spell, in real minutes.</summary>
    [DataField]
    public float MinMinutes = 10f;

    /// <summary>Longest spell, in real minutes.</summary>
    [DataField]
    public float MaxMinutes = 30f;
}

/// <summary>What a weather status effect does to the exposed things on its map, every <see cref="Frequency"/>.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CFWeatherEffectsComponent : Component
{
    [DataField]
    public TimeSpan Frequency = TimeSpan.FromSeconds(10);

    [DataField, AutoPausedField]
    public TimeSpan NextEffect;

    /// <summary>Chance per exposed burning thing to go out.</summary>
    [DataField]
    public float ExtinguishChance;

    /// <summary>Chance per exposed plant to take in <see cref="WaterAmount"/>.</summary>
    [DataField]
    public float WaterChance;

    [DataField]
    public float WaterAmount = 5f;
}

public enum CFTemperatureGearKind : byte
{
    Cold,
    Heat,
}

/// <summary>Worn clothing that eases a harsh climate. Heat gear does nothing over armor.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFTemperatureGearComponent : Component
{
    [DataField(required: true)]
    public CFTemperatureGearKind Kind;
}
