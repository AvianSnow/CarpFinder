using Robust.Shared.Audio;

namespace Content.Server.CF.Temperature;

[RegisterComponent, Access(typeof(CFFireplaceSystem))]
public sealed partial class CFFireplaceComponent : Component
{
    [DataField]
    public string ContainerId = "storagebase";

    [DataField]
    public float MaxFuelLimit = 100f;

    [DataField]
    public float FireFadeDelta = 0.2f;

    [DataField]
    public float Fuel = 10f;

    [DataField]
    public float FuelDrainingPerUpdate = 1f;

    [DataField]
    public bool DeleteOnEmpty;

    [DataField]
    public TimeSpan UpdateFrequency = TimeSpan.FromSeconds(1f);

    [DataField]
    public TimeSpan NextUpdateTime;

    [DataField]
    public SoundSpecifier InsertFuelSound = new SoundPathSpecifier("/Audio/Ambience/Objects/fireplace.ogg");
}
