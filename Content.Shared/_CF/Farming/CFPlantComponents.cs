using Content.Shared.DoAfter;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Maps;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Farming;

/// <summary>
/// A growing plant. Sunlight gives it energy and water gives it resource; growing spends both.
/// Based on CP14's plant components.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class CFPlantComponent : Component
{
    public const string Water = "Water";

    [DataField, AutoNetworkedField]
    public float Energy = 30f;

    [DataField, AutoNetworkedField]
    public float EnergyMax = 100f;

    [DataField, AutoNetworkedField]
    public float Resource = 30f;

    [DataField, AutoNetworkedField]
    public float ResourceMax = 100f;

    /// <summary>From 0 to 1.</summary>
    [DataField, AutoNetworkedField]
    public float GrowthLevel;

    /// <summary>Longest wait between updates, in real seconds. Each wait is random up to this.</summary>
    [DataField]
    public float UpdateFrequency = 60f;

    [DataField, AutoPausedField]
    public TimeSpan NextUpdateTime;

    [DataField]
    public string Solution = "plant";

    /// <summary>Energy gained per update under the sun.</summary>
    [DataField]
    public float SunlightEnergy = 0.5f;

    /// <summary>Most liquid drawn from <see cref="Solution"/> per update. Each unit of water is 1 resource.</summary>
    [DataField]
    public float WaterPerUpdate = 5f;

    [DataField]
    public float EnergyCost = 0.25f;

    [DataField]
    public float ResourceCost = 0.25f;

    [DataField]
    public float GrowthPerUpdate = 0.12f;

    /// <summary>Number of grow-N sprite states.</summary>
    [DataField]
    public int GrowthSteps = 5;
}

/// <summary>Can be picked by hand once grown.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFPlantGatherableComponent : Component
{
    [DataField(required: true)]
    public EntityTableSelector Loot = default!;

    [DataField]
    public float GatherOffset = 0.3f;

    [DataField]
    public TimeSpan GatherDelay = TimeSpan.FromSeconds(1);

    [DataField]
    public float GrowthLevelToHarvest = 0.9f;

    /// <summary>False for a plant that regrows after picking.</summary>
    [DataField]
    public bool DeleteAfterHarvest = true;

    /// <summary>Growth lost to a harvest on a plant that stays.</summary>
    [DataField]
    public float GrowthCostHarvest = 0.4f;

    [DataField]
    public SoundSpecifier GatherSound = new SoundPathSpecifier("/Audio/Effects/Footsteps/grass1.ogg");
}

/// <summary>Grows into <see cref="PlantProto"/> when planted in soil.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFSeedComponent : Component
{
    [DataField(required: true)]
    public EntProtoId PlantProto;

    [DataField]
    public TimeSpan PlantingTime = TimeSpan.FromSeconds(1);

    [DataField]
    public HashSet<ProtoId<ContentTileDefinition>> SoilTile = new() { "CFFloorDirtSeedbed" };
}

public enum CFPlantVisualLayers : byte
{
    Base,
}

[Serializable, NetSerializable]
public sealed partial class CFPlantSeedDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public NetCoordinates Coordinates;

    public CFPlantSeedDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class CFPlantGatherDoAfterEvent : SimpleDoAfterEvent;
