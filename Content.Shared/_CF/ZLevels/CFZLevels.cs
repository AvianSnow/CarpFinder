using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.ZLevels;

/// <summary>
/// On a stair, ladder or hole being placed: makes the matching end on the level at <see cref="Offset"/>.
/// Kept from the CP14 port, so the stairs and ladders already on maps still work on the CrystallEdge network.
/// <c>CFZLevelPortalSystem</c> links them.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFZLevelAutoPortalComponent : Component
{
    /// <summary>-1 is the level below, 1 the level above.</summary>
    [DataField(required: true)]
    public int Offset;

    [DataField(required: true)]
    public EntProtoId OtherSide;
}

/// <summary>
/// Stepping onto this drops a creature to the level below. The drop itself is vertical physics
/// (<c>CFZPhysicsComponent</c>), so the landing hurts as any fall does. An empty tile does the same with no entity.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFZLevelHoleComponent : Component;

/// <summary>
/// A ladder or rope: clicking it Climbs to its linked end. A DC of 0 is a plain move, as the book allows
/// for a trivial climb. Otherwise it is an Athletics check, and a critical failure falls.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFZLevelClimbComponent : Component
{
    [DataField]
    public int Dc;

    [DataField]
    public int Actions = 1;
}

/// <summary>The Player Core falling rules.</summary>
public static class CFFallRules
{
    public const int MaxFeet = 1500;

    /// <summary>How far apart two levels are. One level of vertical physics is this many feet.</summary>
    public const int LevelFeet = 10;

    /// <summary>Half the distance, for any fall over 5 feet. Falls past 1,500 feet count as 1,500.</summary>
    public static int Damage(int feet)
    {
        return feet <= 5 ? 0 : Math.Min(feet, MaxFeet) / 2;
    }

    /// <summary>
    /// The distance a creature fell, from the speed it landed at. Vertical physics measures height in levels and
    /// accelerates at <paramref name="gravity"/> levels per second squared, so the height is v² / 2g levels.
    /// Rounded to the nearest 5 feet, the grid the rules count in.
    /// </summary>
    public static int FeetFromImpact(float impactSpeed, float gravity)
    {
        if (gravity <= 0 || impactSpeed <= 0)
            return 0;

        var levels = impactSpeed * impactSpeed / (2 * gravity);
        return (int)Math.Round(levels * LevelFeet / 5) * 5;
    }
}
