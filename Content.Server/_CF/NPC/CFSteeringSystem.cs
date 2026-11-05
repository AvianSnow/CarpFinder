using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Events;
using Content.Server.NPC.Systems;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Factions;
using Content.Shared.CF.NPC;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CF.NPC;

/// <summary>What a ray touched, and how far along it.</summary>
public readonly record struct CFFeelerHit(EntityUid Entity, float Distance);

/// <summary>
/// Writes steering behaviours into stock's interest map during <see cref="NPCSteeringEvent"/>, as stock juking does.
/// Stock then removes danger from it, so walls and other mobs are still avoided.
/// </summary>
public sealed partial class CFSteeringSystem : EntitySystem
{
    /// <summary>Speed changes smaller than this are not worth refreshing movement for.</summary>
    private const float SpeedStep = 0.05f;

    /// <summary>How close WallFollow lets the creature get.</summary>
    private const float WallOffset = 0.6f;

    private const int WallMask = (int) CollisionGroup.Impassable;

    private const int WallFeelers = 8;

    /// <summary>Danger from a feeler touching something right in front of it. Above 1, so it beats any interest.</summary>
    private const float FeelerDanger = 2f;

    /// <summary>Above the 1 any goal has, so it holds to its side until it is clear.</summary>
    private const float AvoidWeight = 1.2f;

    /// <summary>How close something has to be, as danger, before it commits to going around it.</summary>
    private const float AvoidDanger = 1f;

    private static readonly TimeSpan AvoidTime = TimeSpan.FromSeconds(0.3);

    private const float MinimumEscape = 0.0001f;

    /// <summary>The search has to reach as far as the widest circle speed can give.</summary>
    private const float PanicLeadSearch = 0.2f;

    /// <summary>Well above anything else steering writes, so it wins outright.</summary>
    private const float PanicForce = 4f;

    /// <summary>Added to the search, because a tile's center is further away than its edge.</summary>
    private const float PanicSearchMargin = 0.75f;

    private const LookupFlags PanicLookup = LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sensors;

    /// <summary>What a ray can touch: walls, dangerous tiles, and mobs.</summary>
    private const long FeelerMask = (long) (CollisionGroup.Impassable
                                            | CollisionGroup.HighImpassable
                                            | CollisionGroup.MidImpassable
                                            | CollisionGroup.LowImpassable
                                            | CollisionGroup.MobLayer);

    private const float TileHalfWidth = 0.5f;

    /// <summary>Lava and the like are not hard, which makes them sensors. <c>Static</c> alone does not find them.</summary>
    private const LookupFlags TileLookup = LookupFlags.Static | LookupFlags.Sensors;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private NPCSteeringSystem _npcSteering = default!;
    [Dependency] private RayCastSystem _rays = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFFlankingSystem _flanking = default!;
    [Dependency] private CFStealthSystem _stealth = default!;
    [Dependency] private CFInfluenceMapSystem _influence = default!;

    /// <summary>Starts or changes what this creature is doing. Stock only steers something it has registered.</summary>
    public void Steer(
        EntityUid uid,
        List<CFSteeringBehavior> behaviors,
        EntityUid? target = null,
        Vector2? point = null,
        CFFeltMobs felt = CFFeltMobs.Ignore,
        CFFlowField? field = null)
    {
        var steering = EnsureComp<CFSteeringComponent>(uid);
        steering.Behaviors = behaviors;
        steering.Target = target;
        steering.Point = point;
        steering.FeltMobs = felt;
        steering.Field = field;
        steering.ArriveSpeed = 1f;

        // Stock leaves this set for good, and a creature in it does not steer at all.
        if (TryComp(uid, out NPCSteeringComponent? stock) && stock.Status == SteeringStatus.NoPath)
            _npcSteering.Unregister(uid, stock);

        _npcSteering.Register(uid, Transform(uid).Coordinates);
    }

    /// <summary>Stops moving under these behaviours.</summary>
    public void Stop(Entity<CFSteeringComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.Behaviors = new List<CFSteeringBehavior>();
        ent.Comp.Target = null;
        ent.Comp.Point = null;
        ent.Comp.FeltMobs = CFFeltMobs.Ignore;
        ent.Comp.Field = null;
        ent.Comp.ArriveSpeed = 1f;
        ApplySpeed((ent.Owner, ent.Comp));
        _npcSteering.Unregister(ent);
    }

    [SubscribeLocalEvent]
    private void OnSteering(Entity<CFSteeringComponent> ent, ref NPCSteeringEvent args)
    {
        var usesStockSeek = false;
        foreach (var behavior in ent.Comp.Behaviors)
        {
            if (behavior.Kind == CFSteeringKind.Seek)
            {
                usesStockSeek = true;
                continue;
            }

            Add(ref args, Desired(ent, behavior, args.WorldPosition, args.Steering.LastSteerDirection), behavior.Weight);
        }

        if (ent.Comp.Behaviors.Count > 0 && !usesStockSeek)
            args.Steering.CanSeek = false;

        Feel(ent, ref args);
        ApplySpeed(ent);
    }

    /// <summary>
    /// A three-ray fan along the way it is heading. Stock takes danger away from interest, so a feeler touching
    /// something turns every kind of movement aside. A ray that reaches an enemy adds to the vectors instead.
    /// Rays reach further at speed, which makes their danger work like time to impact.
    /// </summary>
    private void Feel(Entity<CFSteeringComponent> ent, ref NPCSteeringEvent args)
    {
        ent.Comp.Felt = null;
        Array.Clear(ent.Comp.FeelerTarget);

        // Beside or behind it, where the fan cannot reach. Flanking already tracks these.
        foreach (var enemy in _flanking.EnemiesInReach(ent))
        {
            if (CanFeel(ent, enemy))
                FeelMob(ent, ref args, enemy);
        }

        var heading = Heading(ent, ref args);
        if (heading != Vector2.Zero)
            FeelFan(ent, ref args, heading);

        var frameTime = (float) _timing.FrameTime.TotalSeconds;
        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            ent.Comp.FeelerDanger[i] = CFSteering.SmoothDanger(ent.Comp.FeelerDanger[i], ent.Comp.FeelerTarget[i], frameTime);
            args.Steering.Danger[i] = MathF.Max(args.Steering.Danger[i], ent.Comp.FeelerDanger[i]);
        }

        Panic(ent, ref args);
    }

    /// <summary>
    /// The safety circle. Rays run ahead, so a creature sliding alongside lava can miss all three while its body
    /// clips the tile. Anything inside the circle overrides everything else, with no easing, and snaps it away.
    /// </summary>
    private void Panic(Entity<CFSteeringComponent> ent, ref NPCSteeringEvent args)
    {
        var position = args.WorldPosition;
        var velocity = CompOrNull<PhysicsComponent>(ent)?.LinearVelocity ?? Vector2.Zero;
        var escape = Vector2.Zero;
        var search = ent.Comp.PanicRadius + velocity.Length() * PanicLeadSearch + PanicSearchMargin;
        foreach (var other in _lookup.GetEntitiesInRange(ent.Owner, search, PanicLookup))
        {
            if (!IsPanicCause(ent, other))
                continue;

            var closest = CFSteering.ClosestPoint(_lookup.GetWorldAABB(other), position);
            var away = position - closest;

            // Already overlapping, as a mob on a lava tile would be. Its center is the way out.
            if (away == Vector2.Zero)
                away = position - _transform.GetWorldPosition(other);

            // A wall only stops it, so it can come close. Lava it has to turn away from in time.
            var radius = HasComp<CFDangerousTileComponent>(other)
                ? CFSteering.HazardRadius(ent.Comp.PanicRadius, velocity, -away)
                : ent.Comp.PanicRadius;

            var distance = away.Length();
            if (distance > 0f && distance < radius)
                escape += away / distance * (radius - distance);
        }

        if (escape == Vector2.Zero)
            return;

        // Straight back only returns it to the same spot, so it keeps going the way it committed to.
        // The two can cancel out exactly, and normalizing zero gives NaN, which stock's blend then keeps forever.
        if (_timing.CurTime < ent.Comp.AvoidUntil)
        {
            var committed = escape + ent.Comp.AvoidDirection * escape.Length();
            if (committed.LengthSquared() > MinimumEscape)
                escape = committed;
        }

        var norm = Vector2.Normalize(args.OffsetRotation.RotateVec(escape));
        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            var alignment = Vector2.Dot(norm, NPCSteeringSystem.Directions[i]);
            args.Steering.Interest[i] = Math.Max(0f, alignment) * PanicForce;
            args.Steering.Danger[i] = Math.Max(0f, -alignment) * PanicForce;
        }
    }

    /// <summary>
    /// Walls, dangerous tiles and other mobs. Not who it means to reach, or it could never fight them.
    /// </summary>
    private bool IsPanicCause(Entity<CFSteeringComponent> ent, EntityUid other)
    {
        if (other == ent.Owner || other == ent.Comp.Target)
            return false;

        if (HasComp<MobStateComponent>(other))
            return _mobState.IsAlive(other) && !(ent.Comp.FeltMobs == CFFeltMobs.Chase && _factions.CanHarm(ent, other));

        if (HasComp<CFDangerousTileComponent>(other))
            return true;

        return TryComp(other, out PhysicsComponent? body) && body.Hard && body.BodyType == BodyType.Static;
    }

    private void FeelFan(Entity<CFSteeringComponent> ent, ref NPCSteeringEvent args, Vector2 heading)
    {
        var speed = CompOrNull<PhysicsComponent>(ent)?.LinearVelocity.Length() ?? 0f;
        var length = CFSteering.FeelerReach(ent.Comp.FeelerLength, speed, ent.Comp.FeelerFullSpeed, ent.Comp.FeelerMinLength);
        var sideLength = CFSteering.FeelerReach(ent.Comp.FeelerSideLength, speed, ent.Comp.FeelerFullSpeed, ent.Comp.FeelerMinLength);

        var (centre, left, right) = CFSteering.Fan(heading, ent.Comp.FeelerAngle);
        var ahead = FeelAlong(ent, ref args, centre, length);
        var leftDanger = FeelAlong(ent, ref args, left, sideLength);
        var rightDanger = FeelAlong(ent, ref args, right, sideLength);

        // Blocked straight ahead, nothing is left with any interest, and a flat wall would hold it there.
        var now = _timing.CurTime;
        if (ahead >= AvoidDanger && now >= ent.Comp.AvoidUntil)
        {
            ent.Comp.AvoidDirection = new Vector2(centre.Y, -centre.X) * CFSteering.TurnSide(leftDanger, rightDanger);
            ent.Comp.AvoidUntil = now + AvoidTime;
        }

        if (now < ent.Comp.AvoidUntil)
            Add(ref args, ent.Comp.AvoidDirection, AvoidWeight);
    }

    /// <summary>Casts one ray and returns the danger it found.</summary>
    private float FeelAlong(Entity<CFSteeringComponent> ent, ref NPCSteeringEvent args, Vector2 direction, float length)
    {
        if (!TryFeel(ent, args.WorldPosition, direction, ent.Comp.FeelerSight, out var hit))
            return 0f;

        if (HasComp<MobStateComponent>(hit.Entity))
            return FeelMob(ent, ref args, hit.Entity);

        if (hit.Distance >= length)
            return 0f;

        var danger = (1f - hit.Distance / length) * FeelerDanger;
        AddDanger(ent, ref args, direction, danger);
        return danger;
    }

    private float FeelMob(Entity<CFSteeringComponent> ent, ref NPCSteeringEvent args, EntityUid mob)
    {
        ent.Comp.Felt ??= mob;
        var toMob = _transform.GetWorldPosition(mob) - args.WorldPosition;
        if (ent.Comp.FeltMobs == CFFeltMobs.Chase)
            Add(ref args, toMob, 1f);

        if (ent.Comp.FeltMobs != CFFeltMobs.Avoid)
            return 0f;

        AddDanger(ent, ref args, toMob, FeelerDanger);
        return FeelerDanger;
    }

    private static void AddDanger(Entity<CFSteeringComponent> ent, ref NPCSteeringEvent args, Vector2 direction, float danger)
    {
        if (direction == Vector2.Zero)
            return;

        var norm = Vector2.Normalize(args.OffsetRotation.RotateVec(direction));
        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            var result = CFSteering.FeelerSpread(Vector2.Dot(norm, NPCSteeringSystem.Directions[i])) * danger;
            if (result > 0f)
                ent.Comp.FeelerTarget[i] = MathF.Max(ent.Comp.FeelerTarget[i], result);
        }
    }

    /// <summary>
    /// Casts one ray and returns the first thing it touches: a wall or other fixed solid, a dangerous tile
    /// (whose fixture is not hard), or an enemy. Allies, and enemies this creature cannot detect, do not stop it.
    /// </summary>
    public bool TryFeel(EntityUid uid, Vector2 from, Vector2 direction, float length, out CFFeelerHit hit)
    {
        hit = default;
        var filter = new QueryFilter
        {
            MaskBits = FeelerMask,
            Flags = QueryFlags.Static | QueryFlags.Dynamic | QueryFlags.Sensors,
            IsIgnored = other => !CanFeel(uid, other),
        };

        var result = _rays.CastRayClosest(Transform(uid).MapID, from, direction * length, filter);
        if (!result.Hit)
            return false;

        var first = result.Results[0];
        hit = new CFFeelerHit(first.Entity, first.Fraction * length);
        return true;
    }

    /// <summary>The sensors query also finds things like triggers, which are nothing to steer around.</summary>
    private bool CanFeel(EntityUid uid, EntityUid other)
    {
        if (other == uid)
            return false;

        if (HasComp<MobStateComponent>(other))
        {
            return _mobState.IsAlive(other)
                   && _factions.CanHarm(uid, other)
                   && _stealth.Detection(other, uid) != CFDetection.Undetected;
        }

        if (HasComp<CFDangerousTileComponent>(other))
            return true;

        return TryComp(other, out PhysicsComponent? body) && body.Hard && body.BodyType == BodyType.Static;
    }

    /// <summary>Where it is going. Before it has moved, that is what it most wants to do.</summary>
    private Vector2 Heading(EntityUid uid, ref NPCSteeringEvent args)
    {
        var toWorld = -args.OffsetRotation;
        if (args.Steering.LastSteerDirection != Vector2.Zero)
            return toWorld.RotateVec(args.Steering.LastSteerDirection);

        var velocity = CompOrNull<PhysicsComponent>(uid)?.LinearVelocity ?? Vector2.Zero;
        if (velocity != Vector2.Zero)
            return velocity;

        var best = -1;
        var strongest = 0f;
        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            if (args.Steering.Interest[i] <= strongest)
                continue;

            best = i;
            strongest = args.Steering.Interest[i];
        }

        return best < 0 ? Vector2.Zero : toWorld.RotateVec(NPCSteeringSystem.Directions[best]);
    }

    /// <summary>Whether a point is on a dangerous tile.</summary>
    public bool IsDangerous(MapCoordinates point)
    {
        return _lookup.GetEntitiesInRange<CFDangerousTileComponent>(point, TileHalfWidth, TileLookup).Count > 0;
    }

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(Entity<CFSteeringComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = ent.Comp.AppliedSpeed;
        args.ModifySpeed(speed, speed);
    }

    private void ApplySpeed(Entity<CFSteeringComponent> ent)
    {
        var speed = ent.Comp.SpeedMultiplier * ent.Comp.ArriveSpeed;
        if (Math.Abs(speed - ent.Comp.AppliedSpeed) < SpeedStep)
            return;

        ent.Comp.AppliedSpeed = speed;
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private Vector2 Desired(Entity<CFSteeringComponent> ent, CFSteeringBehavior behavior, Vector2 position, Vector2 heading)
    {
        switch (behavior.Kind)
        {
            case CFSteeringKind.Wander:
                ent.Comp.WanderAngle += _random.NextFloat(-behavior.Jitter, behavior.Jitter);
                return CFSteering.Wander(heading, ent.Comp.WanderAngle, behavior.Radius);
            case CFSteeringKind.Cohesion:
                return CFSteering.Seek(position, AlliesCentre(ent, position, behavior.Radius) ?? position);
            case CFSteeringKind.Separation:
                return CFSteering.Flee(position, AlliesCentre(ent, position, behavior.Radius) ?? position);
            case CFSteeringKind.WallFollow:
                return NearestWall(ent, position, behavior.Radius);
            case CFSteeringKind.FieldFollow:
                return _influence.TryFollow(ent, behavior.Layer, ent.Comp.Target, out var trail) ? trail : Vector2.Zero;
            case CFSteeringKind.FlowField:
                return Flow(ent, position);
        }

        if (!TryGetGoal(ent, out var goal, out var velocity))
            return Vector2.Zero;

        var speed = CompOrNull<PhysicsComponent>(ent)?.LinearVelocity.Length() ?? 0f;
        switch (behavior.Kind)
        {
            case CFSteeringKind.Flee:
                return CFSteering.Flee(position, goal);
            case CFSteeringKind.Arrive:
                ent.Comp.ArriveSpeed = CFSteering.ArriveSpeed(position, goal, behavior.Radius);
                return CFSteering.Seek(position, goal);
            case CFSteeringKind.Pursuit:
                return CFSteering.Pursuit(position, speed, goal, velocity);
            case CFSteeringKind.Evade:
                return CFSteering.Evade(position, speed, goal, velocity);
            default:
                return Vector2.Zero;
        }
    }

    /// <summary>The arrow on the square underfoot. Off the edge of the field it heads straight for the goal.</summary>
    private Vector2 Flow(Entity<CFSteeringComponent> ent, Vector2 position)
    {
        var xform = Transform(ent);
        if (ent.Comp.Field is { } field
            && xform.GridUid is { } grid
            && TryComp(grid, out MapGridComponent? gridComp)
            && field.Arrows.TryGetValue(_maps.WorldToTile(grid, gridComp, position), out var arrow))
            return _transform.GetWorldRotation(grid).RotateVec(arrow);

        return TryGetGoal(ent, out var goal, out _) ? CFSteering.Seek(position, goal) : Vector2.Zero;
    }

    private bool TryGetGoal(Entity<CFSteeringComponent> ent, out Vector2 goal, out Vector2 velocity)
    {
        velocity = Vector2.Zero;
        if (ent.Comp.Target is { } target && !TerminatingOrDeleted(target))
        {
            goal = _transform.GetWorldPosition(target);
            velocity = CompOrNull<PhysicsComponent>(target)?.LinearVelocity ?? Vector2.Zero;
            return true;
        }

        goal = ent.Comp.Point ?? Vector2.Zero;
        return ent.Comp.Point != null;
    }

    private Vector2? AlliesCentre(EntityUid uid, Vector2 position, float radius)
    {
        var total = Vector2.Zero;
        var count = 0;
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(_transform.GetMapCoordinates(uid), radius))
        {
            if (mob.Owner == uid || _factions.CanHarm(uid, mob))
                continue;

            total += _transform.GetWorldPosition(mob);
            count++;
        }

        return count == 0 ? null : total / count;
    }

    private Vector2 NearestWall(EntityUid uid, Vector2 position, float radius)
    {
        var map = Transform(uid).MapID;
        var nearest = radius;
        var best = Vector2.Zero;
        for (var i = 0; i < WallFeelers; i++)
        {
            var feeler = new Angle(i * MathF.Tau / WallFeelers).ToVec();
            foreach (var hit in _physics.IntersectRay(map, new CollisionRay(position, feeler, WallMask), radius, uid))
            {
                if (hit.Distance < nearest)
                {
                    nearest = hit.Distance;
                    best = feeler;
                }

                break;
            }
        }

        return nearest <= WallOffset ? Vector2.Zero : best;
    }

    private static void Add(ref NPCSteeringEvent args, Vector2 desired, float weight)
    {
        if (desired == Vector2.Zero || weight <= 0f)
            return;

        var norm = Vector2.Normalize(args.OffsetRotation.RotateVec(desired));
        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            var result = Vector2.Dot(norm, NPCSteeringSystem.Directions[i]) * weight;
            if (result > 0f)
                args.Steering.Interest[i] = MathF.Max(args.Steering.Interest[i], result);
        }
    }
}
