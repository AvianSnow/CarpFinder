using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.NPC;

/// <summary>
/// Spawns an encounter when a player's character comes near, and stays as the group's shared memory.
/// Everything a DM needs is plain text or a number, because VV cannot add to a list.
/// </summary>
[RegisterComponent]
public sealed partial class CFEncounterSpawnerComponent : Component
{
    public static readonly ProtoId<SourcePortPrototype> DiedPort = "CFEncounterMemberDied";
    public static readonly ProtoId<SourcePortPrototype> RoutedPort = "CFEncounterRouted";
    public static readonly ProtoId<SourcePortPrototype> ClearedPort = "CFEncounterCleared";

    /// <summary>The <c>encounter</c> prototype to use.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string Encounter = "SmallGroup";

    /// <summary>Entity ids, separated by commas. Replaces the encounter's mobs when filled in.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string Mobs = string.Empty;

    /// <summary>How many to spawn. 0 uses the encounter's count.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Count;

    /// <summary>How near a player has to come.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float TriggerFeet = 30f;

    /// <summary>Whether it is waiting to spawn. It turns off after spawning. Turn it back on to spawn again.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Armed = true;

    /// <summary>The living members, in the order they take flanking slots.</summary>
    [ViewVariables]
    public List<EntityUid> Members = new();

    /// <summary>How many it spawned.</summary>
    [ViewVariables]
    public int Total;

    [ViewVariables]
    public float RoutAt;

    /// <summary>Whether the group has broken and is running.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Routed;

    /// <summary>The creature the whole group is after.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>Which way around the target the flanking slots start, in radians.</summary>
    public float FlankAngle;

    /// <summary>The arrows the first member builds and the whole group reads.</summary>
    public CFFlowField Field = new();

    public TimeSpan NextCheck;
}

/// <summary>Ties a creature to the spawner that holds its group.</summary>
[RegisterComponent]
public sealed partial class CFEncounterMemberComponent : Component
{
    [ViewVariables]
    public EntityUid Group;
}

/// <summary>Where a group stands around its target.</summary>
public static class CFFlankSlots
{
    /// <summary>
    /// Members pair up on opposite sides, because flanking needs a line through the target.
    /// The first two take one axis, the next two the axis across it.
    /// </summary>
    public static float Angle(int index, float baseAngle)
    {
        return baseAngle + index / 2 * MathF.PI / 2f + index % 2 * MathF.PI;
    }
}
