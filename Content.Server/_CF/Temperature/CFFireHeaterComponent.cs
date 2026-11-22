namespace Content.Server.CF.Temperature;

/// <summary>
/// A fire that heats what is set on it (<c>ItemPlacer</c>) while it burns: food, a cooking pot, a mug of something.
/// After CP14's flammable entity and solution heaters.
/// </summary>
[RegisterComponent, Access(typeof(CFFireHeaterSystem))]
public sealed partial class CFFireHeaterComponent : Component
{
    /// <summary>Degrees each thing on the fire gains every second.</summary>
    [DataField]
    public float DegreesPerSecond = 8f;

    /// <summary>The hottest it makes a thing. Food cooks between 400 and 500.</summary>
    [DataField]
    public float MaxDegrees = 450f;

    /// <summary>The temperature liquids in a thing on it head for: boiling.</summary>
    [DataField]
    public float SolutionDegrees = 373f;

    [DataField]
    public TimeSpan NextUpdate;
}
