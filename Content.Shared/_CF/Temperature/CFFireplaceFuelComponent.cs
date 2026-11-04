namespace Content.Shared.CF.Temperature;

[RegisterComponent]
public sealed partial class CFFireplaceFuelComponent : Component
{
    [DataField]
    public float Fuel = 10f;
}
