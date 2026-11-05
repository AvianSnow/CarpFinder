using System.Numerics;
using Content.Shared.NPC;

namespace Content.Server.CF.NPC;

public enum CFSteeringKind : byte
{
    /// <summary>Stock path-following to the steering coordinates. Without this, stock seek is off.</summary>
    Seek,
    Flee,
    Arrive,
    Wander,
    Pursuit,
    Evade,

    /// <summary>Toward nearby allies.</summary>
    Cohesion,

    /// <summary>Away from nearby allies.</summary>
    Separation,

    /// <summary>Toward the nearest wall, stopping just short of it.</summary>
    WallFollow,

    /// <summary>Along the trail on <see cref="CFSteeringBehavior.Layer"/>, left by the target if there is one.</summary>
    FieldFollow,

    /// <summary>Stay put. This also keeps stock from pathing, as it would for melee.</summary>
    Hold,

    /// <summary>Follow the arrow on the square underfoot, in <see cref="CFSteeringComponent.Field"/>.</summary>
    FlowField,
}

/// <summary>What a creature does about an enemy its feelers touch.</summary>
public enum CFFeltMobs : byte
{
    Ignore,

    /// <summary>Adds interest toward it.</summary>
    Chase,

    /// <summary>Adds danger toward it.</summary>
    Avoid,
}

/// <summary>One weighted steering behaviour.</summary>
[DataDefinition]
public sealed partial class CFSteeringBehavior
{
    [DataField(required: true)]
    public CFSteeringKind Kind;

    [DataField]
    public float Weight = 1f;

    /// <summary>Arrive's slowing radius, the distance Cohesion and Separation look, Wander's circle, or how far WallFollow feels.</summary>
    [DataField]
    public float Radius = 3f;

    /// <summary>How far Wander's angle can jump each tick, in radians.</summary>
    [DataField]
    public float Jitter = 0.5f;

    /// <summary>The influence layer FieldFollow reads.</summary>
    [DataField]
    public CFInfluenceLayer Layer = CFInfluenceLayer.Scent;
}

/// <summary>
/// Adds to stock context steering for an advanced NPC, so doors, walls and collisions keep working.
/// Only creatures set up for GOAP have this. Simple mobs stay on stock steering.
/// </summary>
[RegisterComponent]
public sealed partial class CFSteeringComponent : Component
{
    /// <summary>What it is doing now. GOAP actions swap these.</summary>
    [DataField]
    public List<CFSteeringBehavior> Behaviors = new();

    /// <summary>Scales its speed. Personalities set this.</summary>
    [DataField]
    public float SpeedMultiplier = 1f;

    /// <summary>How far a ray can spot an enemy, in tiles. Walls stop it, so it only sees what is in view.</summary>
    [DataField]
    public float FeelerSight = 12f;

    /// <summary>What it does about enemies it touches. GOAP steps set this.</summary>
    public CFFeltMobs FeltMobs;

    /// <summary>The enemy a ray touched on the last steering tick.</summary>
    public EntityUid? Felt;

    /// <summary>How far the center feeler reaches, in tiles. Obstacles past this do not count.</summary>
    [DataField]
    public float FeelerLength = 2.5f;

    /// <summary>How far the two side feelers reach, in tiles.</summary>
    [DataField]
    public float FeelerSideLength = 1.75f;

    /// <summary>How far the side feelers angle out from the center one, in degrees.</summary>
    [DataField]
    public float FeelerAngle = 30f;

    /// <summary>The speed at which feelers reach their full length. Faster creatures reach further.</summary>
    [DataField]
    public float FeelerFullSpeed = 4.5f;

    /// <summary>The shortest a feeler gets, in tiles, so a creature standing still can still feel a wall.</summary>
    [DataField]
    public float FeelerMinLength = 1f;

    /// <summary>
    /// The safety circle, in tiles from its center. A wall, dangerous tile or other mob inside this overrides all
    /// other steering and snaps it away. A mob's own radius is 0.35, so this means "about to touch". It has to stay
    /// under 0.5: the middle of a one-tile doorway is that far from each side, and any more would hold it there.
    /// </summary>
    [DataField]
    public float PanicRadius = 0.42f;

    /// <summary>
    /// The way it committed to going around something, until <see cref="AvoidUntil"/>. Without this the goal pulls
    /// it straight back into whatever it just turned away from, and it bounces there for good.
    /// </summary>
    public Vector2 AvoidDirection;

    public TimeSpan AvoidUntil;

    /// <summary>What the feelers report this tick, for each stock steering direction.</summary>
    public readonly float[] FeelerTarget = new float[SharedNPCSteeringSystem.InterestDirections];

    /// <summary>The same, eased over a few frames.</summary>
    public readonly float[] FeelerDanger = new float[SharedNPCSteeringSystem.InterestDirections];

    /// <summary>The creature that behaviours chase or run from.</summary>
    public EntityUid? Target;

    /// <summary>A world position to use when there is no <see cref="Target"/>.</summary>
    public Vector2? Point;

    /// <summary>The arrows it is reading. A group shares one.</summary>
    public CFFlowField? Field;

    /// <summary>A door or window it means to get through, so its feelers and safety circle leave it alone.</summary>
    public EntityUid? Through;

    public float WanderAngle;

    /// <summary>The share of full speed Arrive wants now.</summary>
    public float ArriveSpeed = 1f;

    /// <summary>The speed last sent to the movement system, so it only refreshes on a real change.</summary>
    public float AppliedSpeed = 1f;
}
