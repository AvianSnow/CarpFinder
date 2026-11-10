using System.Numerics;
using Content.Server.CF.GOAP.Influence;
using Content.Shared.NPC;
using Robust.Shared.Physics.Components;

namespace Content.Server.CF.GOAP.Steering;

public enum CFGOAPSteeringKind : byte
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

    /// <summary>Along the trail on <see cref="CFGOAPSteeringBehavior.Layer"/>, left by the target if there is one.</summary>
    FieldFollow,

    /// <summary>Stay put. This also keeps stock from pathing, as it would for melee.</summary>
    Hold,

    /// <summary>Follow the arrow on the square underfoot, in <see cref="CFGOAPSteeringComponent.Field"/>.</summary>
    FlowField,
}

/// <summary>What a creature does about an enemy its feelers touch.</summary>
public enum CFGOAPFeltMobs : byte
{
    Ignore,

    /// <summary>Adds interest toward it.</summary>
    Chase,

    /// <summary>Adds danger toward it.</summary>
    Avoid,
}

/// <summary>
/// A wall, other fixed solid or dangerous tile near the creature, with its bounds on the grid. These do not move on
/// their grid, so the safety circle measures to the stored bounds instead of asking the entity every tick.
/// </summary>
public readonly record struct CFGOAPPanicFixed(EntityUid Uid, PhysicsComponent? Body, Box2 Bounds, bool Dangerous, bool Climbable);

/// <summary>One weighted steering behaviour.</summary>
[DataDefinition]
public sealed partial class CFGOAPSteeringBehavior
{
    [DataField(required: true)]
    public CFGOAPSteeringKind Kind;

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
    public CFGOAPInfluenceLayer Layer = CFGOAPInfluenceLayer.Scent;
}

/// <summary>
/// Adds to stock context steering for an advanced NPC, so doors, walls and collisions keep working.
/// Only creatures set up for GOAP have this. Simple mobs stay on stock steering.
/// </summary>
[RegisterComponent]
public sealed partial class CFGOAPSteeringComponent : Component
{
    /// <summary>What it is doing now. GOAP actions swap these.</summary>
    [DataField]
    public List<CFGOAPSteeringBehavior> Behaviors = new();

    /// <summary>Scales its speed. Personalities set this.</summary>
    [DataField]
    public float SpeedMultiplier = 1f;

    /// <summary>How far a ray can spot an enemy, in tiles. Walls stop it, so it only sees what is in view.</summary>
    [DataField]
    public float FeelerSight = 12f;

    /// <summary>What it does about enemies it touches. GOAP steps set this.</summary>
    public CFGOAPFeltMobs FeltMobs;

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

    /// <summary>Steering ticks left before the feelers and the safety circle look again.</summary>
    public int FeelCountdown;

    /// <summary>Its behaviours changed, so the feelers look again at once, far rays included.</summary>
    public bool FeelForced;

    /// <summary>
    /// The enemy each ray of the fan saw past its feeler's reach, out to <see cref="FeelerSight"/>: center, left,
    /// right. Long rays are cast less often than short ones, and this holds what they saw in between.
    /// </summary>
    public readonly EntityUid?[] SightMobs = new EntityUid?[3];

    /// <summary>When the fan next casts its long rays.</summary>
    public TimeSpan NextSight;

    /// <summary>The way it was heading when the feelers last looked. Turning away from it makes them look again.</summary>
    public Vector2 FeltHeading;

    /// <summary>
    /// Enemies the fan's rays touched when they last looked, once per ray, so chasing or avoiding them carries on
    /// between looks.
    /// </summary>
    public List<EntityUid> FanMobs = new();

    /// <summary>
    /// Mobs the safety circle may have to steer around, found when it last looked, with room for how far they can
    /// move before the next look. Distances to them are measured every tick.
    /// </summary>
    public List<EntityUid> PanicMobs = new();

    /// <summary>Walls, fixed solids and dangerous tiles within <see cref="PanicRange"/> of <see cref="PanicCentre"/>.</summary>
    public List<CFGOAPPanicFixed> PanicFixed = new();

    /// <summary>The grid <see cref="PanicFixed"/> is on.</summary>
    public EntityUid? PanicGrid;

    /// <summary>Where on the grid it looked for <see cref="PanicFixed"/>. Walking too far from here looks again.</summary>
    public Vector2 PanicCentre;

    public float PanicRange;

    /// <summary>When it looks for fixed things again anyway, in case a door shut or a wall went up.</summary>
    public TimeSpan PanicRefreshAt;

    /// <summary>A dangerous tile is close, so the feelers look every tick.</summary>
    public bool NearDanger;

    /// <summary>What the feelers report this tick, for each stock steering direction.</summary>
    public readonly float[] FeelerTarget = new float[SharedNPCSteeringSystem.InterestDirections];

    /// <summary>The same, eased over a few frames.</summary>
    public readonly float[] FeelerDanger = new float[SharedNPCSteeringSystem.InterestDirections];

    /// <summary>The creature that behaviours chase or run from.</summary>
    public EntityUid? Target;

    /// <summary>A world position to use when there is no <see cref="Target"/>.</summary>
    public Vector2? Point;

    /// <summary>The arrows it is reading. A group shares one.</summary>
    public CFGOAPFlowField? Field;

    /// <summary>A door or window it means to get through, so its feelers and safety circle leave it alone.</summary>
    public EntityUid? Through;

    public float WanderAngle;

    /// <summary>The share of full speed Arrive wants now.</summary>
    public float ArriveSpeed = 1f;

    /// <summary>The speed last sent to the movement system, so it only refreshes on a real change.</summary>
    public float AppliedSpeed = 1f;

    /// <summary>When it next checks whether the way ahead is clear.</summary>
    public TimeSpan NextNavigate;

    /// <summary>Whether it is following a flow field rather than heading straight there.</summary>
    public bool UsingField;

    /// <summary>The tile underfoot had an arrow on this steering tick, so feelers leave what is fixed to the arrows.</summary>
    public bool OnArrows;

    /// <summary>The door or window it is dealing with.</summary>
    public EntityUid? Obstacle;

    /// <summary>When it turned to that obstacle. Its patience runs from here.</summary>
    public TimeSpan ObstacleStart;

    /// <summary>The field that led it there, which is where giving up on it gets written down.</summary>
    public CFGOAPFlowField? ObstacleField;

    /// <summary>
    /// It opened <see cref="Obstacle"/>, a door, and is walking through to <see cref="PassTile"/>. Once there, its
    /// field is built again from the far side.
    /// </summary>
    public bool PassingThrough;

    /// <summary>The tile past the door, on the grid.</summary>
    public Vector2i PassTile;

    /// <summary>When it stops trying to get through and carries on from wherever it is.</summary>
    public TimeSpan PassUntil;

    /// <summary>
    /// Its goal is calm, such as idling. Its feelers cast no long rays then, and none at all on open floor.
    /// <see cref="CFGOAPSystem"/> sets this.
    /// </summary>
    public bool Calm;

    /// <summary>
    /// Something that is not fixed down but would stop its body, such as a crate, was near when it last looked for
    /// fixed things. Floor with one of these is not open, so a calm creature still feels its way.
    /// </summary>
    public bool LooseObstacleNear;

    /// <summary>Whether it has started swinging at the obstacle or climbing it.</summary>
    public bool Working;

    /// <summary>It just finished with an obstacle and has to set off again.</summary>
    public bool Rerouted;

    /// <summary>It gave up and found no other way, so until this time it stops trying to get there.</summary>
    public TimeSpan NoWayUntil;

    /// <summary>The whole way to a far place, as grid tiles. Empty when it does not need one.</summary>
    public List<Vector2i> Route = new();

    /// <summary>The grid <see cref="Route"/> is on.</summary>
    public EntityUid? RouteGrid;

    /// <summary>The place <see cref="Route"/> leads to, in world space.</summary>
    public Vector2 RouteGoal;

    /// <summary>The route tile it has got to.</summary>
    public int RouteAt;

    /// <summary>The route tile its field is aimed at.</summary>
    public int RouteStop;

    /// <summary>When it may search for a route again.</summary>
    public TimeSpan NextRoute;

    /// <summary>The field of a creature with no group. In a group, the spawner holds one for all of them.</summary>
    public CFGOAPFlowField OwnField = new();
}
