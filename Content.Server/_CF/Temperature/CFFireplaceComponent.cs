using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

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

    /// <summary>What a piece of burnt fuel leaves beside the fire, for the soil. Null for nothing.</summary>
    [DataField]
    public EntProtoId? Ash = "CFAsh1";

    /// <summary>The chance each piece of fuel leaves some.</summary>
    [DataField]
    public float AshChance = 1f;

    [DataField]
    public TimeSpan UpdateFrequency = TimeSpan.FromSeconds(1f);

    [DataField]
    public TimeSpan NextUpdateTime;

    [DataField]
    public SoundSpecifier InsertFuelSound = new SoundPathSpecifier("/Audio/Ambience/Objects/fireplace.ogg");
}
