using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.Adventure;

/// <summary>
/// On a station. Every so often, spawns one mob from <see cref="Table"/> on an outdoor tile of the station's grids,
/// away from every player. Add it through a gameMap's station <c>components</c>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CFWildSpawnerComponent : Component
{
    /// <summary>What can spawn. Each spawn rolls the table once.</summary>
    [DataField(required: true)]
    public EntityTableSelector Table = default!;

    /// <summary>Time between spawns. The first comes this long after the round starts.</summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromMinutes(10);

    /// <summary>Tiles a mob may appear on. Buildings use other floors, so mobs never appear inside one.</summary>
    [DataField]
    public HashSet<ProtoId<ContentTileDefinition>> Tiles = new()
    {
        "CFFloorGrass",
        "CFFloorGrassLight",
        "CFFloorGrassTall",
        "FloorCave",
    };

    /// <summary>No mob appears within this many tiles of a living player.</summary>
    [DataField]
    public float MinPlayerDistance = 30f;

    /// <summary>A spawn is skipped while this many of its mobs are still alive.</summary>
    [DataField]
    public int MaxAlive = 10;

    [DataField, AutoPausedField]
    public TimeSpan NextSpawn;

    /// <summary>Mobs this spawner made, so <see cref="MaxAlive"/> can be counted.</summary>
    [DataField]
    public List<EntityUid> Spawned = new();
}
