using Content.Shared.Whitelist;

namespace Content.Shared.CF.Effects;

/// <summary>
/// The source affects every creature in an area on the map grid (1 tile = 5 feet).
/// Spells, bomb splash, traps, hazards, auras, and breath weapons use it.
/// </summary>
[RegisterComponent]
public sealed partial class CFAreaEffectComponent : Component
{
    [DataField(required: true)]
    public CFAreaShape Shape = CFAreaShape.Burst;

    [DataField(required: true)]
    public int SizeFeet = 5;

    /// <summary>Where the area starts. <see cref="CFAreaOrigin.Auto"/> picks from the shape.</summary>
    [DataField]
    public CFAreaOrigin Origin = CFAreaOrigin.Auto;

    /// <summary>The user's own square counts (emanations) and the user can be affected.</summary>
    [DataField]
    public bool IncludeSelf;

    [DataField]
    public CFTargetFilter Filter = CFTargetFilter.Enemies;

    /// <summary>0 means no limit.</summary>
    [DataField]
    public int MaxTargets;

    /// <summary>Only these entities are affected. Creatures (MobState) are always required.</summary>
    [DataField]
    public EntityWhitelist? Whitelist;
}

public enum CFAreaShape : byte
{
    Burst,
    Cone,
    Emanation,
    Line,
    Square,
}

public enum CFAreaOrigin : byte
{
    /// <summary>Bursts and squares are aimed at a point. Cones, lines, and emanations start at the user.</summary>
    Auto,
    User,
    Point,
}

public static class CFAreaShapes
{
    public static bool StartsAtPoint(this CFAreaEffectComponent area)
    {
        return area.Origin switch
        {
            CFAreaOrigin.User => false,
            CFAreaOrigin.Point => true,
            _ => area.Shape is CFAreaShape.Burst or CFAreaShape.Square,
        };
    }
}
