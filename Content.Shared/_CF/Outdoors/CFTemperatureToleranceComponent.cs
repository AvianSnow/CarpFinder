namespace Content.Shared.CF.Outdoors;

/// <summary>
/// How much less severe this creature finds environmental heat and cold, in steps of the temperature table. A negative
/// value is one step worse. Forge Dwarves and Arctic Elves have one step; a Desert Rat has one of heat and minus one of cold.
/// Read by <c>CFOutdoorsWorldSystem</c> through <see cref="CFEnvironmentRules.Eased"/>.
/// </summary>
[RegisterComponent]
public sealed partial class CFTemperatureToleranceComponent : Component
{
    [DataField]
    public int HeatSteps;

    [DataField]
    public int ColdSteps;
}
