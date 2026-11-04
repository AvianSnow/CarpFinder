namespace Content.Server._CF.Temperature;

[RegisterComponent, AutoGenerateComponentPause, Access(typeof(CFFireSpreadSystem))]
public sealed partial class CFAutoIgniteComponent : Component
{
    [DataField]
    public float StartStack = 1f;

    [DataField]
    public TimeSpan IgniteDelay = TimeSpan.FromSeconds(1f);

    [DataField]
    public TimeSpan IgniteTime = TimeSpan.Zero;
}
