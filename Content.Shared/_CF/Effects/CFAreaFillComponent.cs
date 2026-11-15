using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>
/// What an area leaves on the tiles it covers, and how it gets there: flames, sparks, a gust, a puddle. Shared by
/// <see cref="CFAreaFillComponent"/> and <see cref="CFFillEffect"/>.
/// </summary>
[DataDefinition]
public sealed partial class CFFill
{
    /// <summary>An effect entity spawned on each tile, such as <c>CFFillFlame</c>. It removes itself when it is done.</summary>
    [DataField]
    public EntProtoId? Tile;

    /// <summary>A puddle of this reagent on each tile. Liquid only goes where there is floor.</summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Reagent;

    /// <summary>Units per tile. 30 to 49 draws as one smooth sheet; 50 or more overflows and spreads past the area.</summary>
    [DataField]
    public float UnitsPerTile = 48f;

    /// <summary>How long it takes to reach the farthest tile, nearest first. 0 is all at once.</summary>
    [DataField]
    public float SpreadSeconds = 0.25f;

    /// <summary>
    /// How long the area stays. While it does, what it left is kept, and removed at the end. A creature that moves
    /// faster than a walk on it rolls against the source again, at most once every <see cref="RerollSeconds"/>. A
    /// puddle that is mopped up takes its tile out. 0 means nothing lasts beyond the tile effects' own time.
    /// </summary>
    [DataField]
    public float LingerSeconds;

    [DataField]
    public float RerollSeconds = 3f;

    /// <summary>The tile effects take the source's colour (<see cref="CFVfxSystem.SpellColor"/>).</summary>
    [DataField]
    public bool Tint;
}

public enum CFFillRolls : byte
{
    /// <summary>Everyone it can reach rolls the moment it is cast. What spreads afterwards is only seen.</summary>
    AtOnce,

    /// <summary>Nobody rolls until it has reached its farthest tile. Then whoever stands in it does.</summary>
    WhenFilled,
}

/// <summary>
/// The source's area fills with something that can be seen: fire for Fireball, slick puddles for Grease, liquid for a
/// breath. It goes beside <see cref="CFAreaEffectComponent"/> on a spell, breath, bomb or trap, and
/// <see cref="CFEffectSystem.ResolveArea"/> hands over to <see cref="CFAreaFillSystem"/>. It spreads from where the area
/// starts and stops at walls, and only creatures on the tiles it reaches are affected. For a cloud of gas, use
/// <c>CFBreathGas</c> instead.
/// </summary>
[RegisterComponent]
public sealed partial class CFAreaFillComponent : Component
{
    [IncludeDataField]
    public CFFill Fill = new();

    [DataField]
    public CFFillRolls Rolls = CFFillRolls.AtOnce;
}

/// <summary>
/// A fill that is spreading or lingering, on an entity of its own in nullspace, so it outlives its caster. Server only.
/// </summary>
[RegisterComponent]
public sealed partial class CFActiveFillComponent : Component
{
    /// <summary>What rolls against creatures: its area, check and effects. Null for a fill that is only seen.</summary>
    [DataField]
    public EntityUid? Source;

    [DataField]
    public EntityUid User;

    [DataField]
    public EntityUid Grid;

    [DataField]
    public CFFill Fill = new();

    [DataField]
    public Color? Color;

    [DataField]
    public TimeSpan Start;

    /// <summary>When a lingering fill ends.</summary>
    [DataField]
    public TimeSpan End;

    [DataField]
    public TimeSpan NextWatch;

    /// <summary>Everyone rolls once it has filled.</summary>
    [DataField]
    public bool RollWhenFilled;

    /// <summary>The tiles it reaches, nearest first, each with how long after <see cref="Start"/> it gets there.</summary>
    [DataField]
    public List<CFFillTile> Tiles = new();

    /// <summary>How many of <see cref="Tiles"/> have been filled.</summary>
    [DataField]
    public int Filled;

    /// <summary>What was put on the tiles, to remove when a lingering fill ends.</summary>
    [DataField]
    public List<EntityUid> Spawned = new();

    /// <summary>The puddle on each tile of a lingering liquid fill.</summary>
    public Dictionary<Vector2i, EntityUid> Puddles = new();

    /// <summary>When each creature may next have to roll for moving on it.</summary>
    public Dictionary<EntityUid, TimeSpan> NextRoll = new();
}

[DataDefinition]
public partial struct CFFillTile
{
    [DataField]
    public Vector2i Tile;

    /// <summary>Seconds after the start.</summary>
    [DataField]
    public float Delay;

    public CFFillTile(Vector2i tile, float delay)
    {
        Tile = tile;
        Delay = delay;
    }
}

/// <summary>
/// Fills a small area around the target or the aimed point with something that is only seen: the splash a bomb or a
/// glob of acid leaves. Nobody rolls for it. Put it in any effect list.
/// </summary>
public sealed partial class CFFillEffect : CFEffect
{
    [DataField]
    public CFAreaShape Shape = CFAreaShape.Emanation;

    [DataField]
    public int SizeFeet = 5;

    [DataField]
    public CFEffectAnchor At = CFEffectAnchor.Target;

    // Not an included CFFill: an included definition is read with this effect's !type tag and fails.
    [DataField]
    public EntProtoId? Tile;

    [DataField]
    public ProtoId<ReagentPrototype>? Reagent;

    [DataField]
    public float UnitsPerTile = 15f;

    [DataField]
    public float SpreadSeconds;

    /// <summary>How long what it left stays before it is removed. 0 leaves it to burn out or dry up by itself.</summary>
    [DataField]
    public float LingerSeconds;

    [DataField]
    public bool Tint;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (CFEffectAnchors.Resolve(entities, args, At) is not { } coords)
            return;

        var center = entities.System<SharedTransformSystem>().ToMapCoordinates(coords);
        var fill = new CFFill
        {
            Tile = Tile,
            Reagent = Reagent,
            UnitsPerTile = UnitsPerTile,
            SpreadSeconds = SpreadSeconds,
            LingerSeconds = LingerSeconds,
            Tint = Tint,
        };
        entities.System<CFAreaFillSystem>().Splash(args.Source, args.User, center, Shape, SizeFeet, fill);
    }
}
