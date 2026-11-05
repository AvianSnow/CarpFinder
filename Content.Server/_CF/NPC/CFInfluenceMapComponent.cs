using System.Numerics;

namespace Content.Server.CF.NPC;

public enum CFInfluenceLayer : byte
{
    /// <summary>The trail creatures leave.</summary>
    Scent,

    /// <summary>One-off spikes from fighting and speaking.</summary>
    Noise,

    /// <summary>Danger, for creatures that are running away.</summary>
    Threat,

    /// <summary>Where allies are.</summary>
    Ally,
}

/// <summary>One square. It records when something was last here and which way it was heading.</summary>
public struct CFInfluenceCell
{
    public float Intensity;

    public TimeSpan LastVisit;

    /// <summary>Grid-local and normalised. Zero if the creature was not moving.</summary>
    public Vector2 Heading;

    public EntityUid Emitter;
}

/// <summary>An 8×8 block of squares, only allocated once something paints there.</summary>
public sealed class CFInfluenceChunk
{
    public const int Size = 8;

    public readonly CFInfluenceCell[] Cells = new CFInfluenceCell[Size * Size];
}

/// <summary>
/// An invisible grid that creatures paint on as they move. Each grid has its own, so Z-levels stay separate.
/// It only exists on a grid with an advanced NPC, because nothing else reads it.
/// </summary>
[RegisterComponent]
public sealed partial class CFInfluenceMapComponent : Component
{
    public readonly Dictionary<(CFInfluenceLayer Layer, Vector2i Chunk), CFInfluenceChunk> Chunks = new();

    /// <summary>How long it takes a mark to fade to half strength.</summary>
    [DataField]
    public float HalfLifeSeconds = 60f;

    public TimeSpan NextUpdate;
}

/// <summary>Something that leaves a mark on the influence map as it moves.</summary>
[RegisterComponent]
public sealed partial class CFScentEmitterComponent : Component
{
    [DataField]
    public CFInfluenceLayer Layer = CFInfluenceLayer.Scent;

    [DataField]
    public float Strength = 1f;

    /// <summary>The mark reaches this many squares around the creature.</summary>
    [DataField]
    public int Radius;

    public EntityUid? LastGrid;

    public Vector2i LastTile;
}
