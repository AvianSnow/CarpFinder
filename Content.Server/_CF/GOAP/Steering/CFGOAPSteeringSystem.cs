using System.Numerics;
using Content.Server.CF.GOAP.Influence;
using Content.Server.NPC.Components;
using Content.Server.NPC.Events;
using Content.Server.NPC.Systems;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Factions;
using Content.Shared.CF.GOAP;
using Content.Shared.Climbing.Components;
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

namespace Content.Server.CF.GOAP.Steering;

/// <summary>What a ray touched, and how far along it.</summary>
public readonly record struct CFGOAPFeelerHit(EntityUid Entity, float Distance);

/// <summary>
/// Writes steering behaviours into stock's interest map during <see cref="NPCSteeringEvent"/>, as stock juking does.
/// Stock then removes danger from it, so walls and other mobs are still avoided.
/// </summary>
public sealed partial class CFGOAPSteeringSystem : EntitySystem
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
    [Dependency] private CFGOAPInfluenceMapSystem _influence = default!;

    /// <summary>Starts or changes what this creature is doing. Stock only steers something it has registered.</summary>
    public void Steer(
        EntityUid uid,
        List<CFGOAPSteeringBehavior> behaviors,
        EntityUid? target = null,
        Vector2? point = null,
        CFGOAPFeltMobs felt = CFGOAPFeltMobs.Ignore,
        CFGOAPFlowField? field = null,
        EntityUid? through = null)
    {
        var steering = EnsureComp<CFGOAPSteeringComponent>(uid);
        steering.Behaviors = behaviors;
        steering.Target = target;
        steering.Point = point;
        steering.FeltMobs = felt;
        steering.Field = field;
        steering.Through = through;
        steering.ArriveSpeed = 1f;

        // Stock leaves this set for good, and a creature in it does not steer at all.
        if (TryComp(uid, out NPCSteeringComponent? stock) && stock.Status == SteeringStatus.NoPath)
            _npcSteering.Unregister(uid, stock);

        _npcSteering.Register(uid, Transform(uid).Coordinates);
    }

    /// <summary>Stays where it is. This also keeps stock from walking it after a melee target.</summary>
    public void Hold(EntityUid uid, EntityUid? through = null)
    {
        Steer(uid, new List<CFGOAPSteeringBehavior> { new() { Kind = CFGOAPSteeringKind.Hold } }, through: through);
    }

    /// <summary>Stops moving under these behaviours, and drops any door or window it was dealing with.</summary>
    public void Stop(Entity<CFGOAPSteeringComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (ent.Comp.Working)
            RemComp<NPCMeleeCombatComponent>(ent);

        ent.Comp.UsingField = false;
        ent.Comp.Obstacle = null;
        ent.Comp.ObstacleField = null;
        ent.Comp.Working = false;
        ent.Comp.Rerouted = false;
        ent.Comp.Behaviors = new List<CFGOAPSteeringBehavior>();
        ent.Comp.Target = null;
        ent.Comp.Point = null;
        ent.Comp.FeltMobs = CFGOAPFeltMobs.Ignore;
        ent.Comp.Field = null;
        ent.Comp.Through = null;
        ent.Comp.ArriveSpeed = 1f;
        ent.Comp.Route.Clear();
        ApplySpeed((ent.Owner, ent.Comp));
        _npcSteering.Unregister(ent);
    }

    [SubscribeLocalEvent]
    private void OnSteering(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args)
    {
        var usesStockSeek = false;
        foreach (var behavior in ent.Comp.Behaviors)
        {
            if (behavior.Kind == CFGOAPSteeringKind.Seek)
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
    private void Feel(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args)
    {
        ent.Comp.Felt = null;
        ent.Comp.OnArrows = OnArrows(ent, args.WorldPosition);
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
            ent.Comp.FeelerDanger[i] = CFGOAPSteeringMath.SmoothDanger(ent.Comp.FeelerDanger[i], ent.Comp.FeelerTarget[i], frameTime);
            args.Steering.Danger[i] = MathF.Max(args.Steering.Danger[i], ent.Comp.FeelerDanger[i]);
        }

        Panic(ent, ref args);
    }

    /// <summary>
    /// The safety circle. Rays run ahead, so a creature sliding alongside lava can miss all three while its body
    /// clips the tile. Anything inside the circle overrides everything else, with no easing, and snaps it away.
    /// </summary>
    private void Panic(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args)
    {
        var position = args.WorldPosition;
        var velocity = CompOrNull<PhysicsComponent>(ent)?.LinearVelocity ?? Vector2.Zero;
        var escape = Vector2.Zero;
        var onlyWalls = true;
        var search = ent.Comp.PanicRadius + velocity.Length() * PanicLeadSearch + PanicSearchMargin;
        foreach (var other in _lookup.GetEntitiesInRange(ent.Owner, search, PanicLookup))
        {
            if (!IsPanicCause(ent, other))
                continue;

            var closest = CFGOAPSteeringMath.ClosestPoint(_lookup.GetWorldAABB(other), position);
            var away = position - closest;

            // Already overlapping, as a mob on a lava tile would be. Its center is the way out.
            if (away == Vector2.Zero)
                away = position - _transform.GetWorldPosition(other);

            // A wall only stops it, so it can come close. Lava it has to turn away from in time.
            var radius = HasComp<CFDangerousTileComponent>(other)
                ? CFGOAPSteeringMath.HazardRadius(ent.Comp.PanicRadius, velocity, -away)
                : ent.Comp.PanicRadius;

            var distance = away.Length();
            if (distance > 0f && distance < radius)
            {
                escape += away / distance * (radius - distance);
                onlyWalls &= !HasComp<MobStateComponent>(other) && !HasComp<CFDangerousTileComponent>(other);
            }
        }

        if (escape == Vector2.Zero)
            return;

        // A wall cannot hurt it, so it slides along instead of being thrown back. Thrown straight off the corner of a
        // doorway, it comes back and rattles there for good.
        var wanted = onlyWalls ? Wanted(ref args) : Vector2.Zero;
        if (wanted != Vector2.Zero)
        {
            var slide = escape + Vector2.Normalize(wanted) * escape.Length();
            if (slide.LengthSquared() > MinimumEscape)
                escape = slide;
        }

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

    /// <summary>Where its behaviours want to go this tick, in world space.</summary>
    private static Vector2 Wanted(ref NPCSteeringEvent args)
    {
        var wanted = Vector2.Zero;
        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            wanted += NPCSteeringSystem.Directions[i] * args.Steering.Interest[i];
        }

        return (-args.OffsetRotation).RotateVec(wanted);
    }

    /// <summary>
    /// Walls, dangerous tiles and other mobs. Not who it means to reach, or it could never fight them.
    /// </summary>
    private bool IsPanicCause(Entity<CFGOAPSteeringComponent> ent, EntityUid other)
    {
        if (other == ent.Owner || other == ent.Comp.Target || other == ent.Comp.Through || IsClimbingOver(ent, other))
            return false;

        if (HasComp<MobStateComponent>(other))
            return _mobState.IsAlive(other) && !(ent.Comp.FeltMobs == CFGOAPFeltMobs.Chase && _factions.CanHarm(ent, other));

        if (HasComp<CFDangerousTileComponent>(other))
            return true;

        // Busy with an obstacle, it stays put. What stands beside it cannot hurt it, and being thrown clear of the next
        // table along would only end the climb, over and over.
        if (ent.Comp.Through != null || ent.Comp.OnArrows)
            return false;

        return IsFixed(other);
    }

    private void FeelFan(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args, Vector2 heading)
    {
        var speed = CompOrNull<PhysicsComponent>(ent)?.LinearVelocity.Length() ?? 0f;
        var length = CFGOAPSteeringMath.FeelerReach(ent.Comp.FeelerLength, speed, ent.Comp.FeelerFullSpeed, ent.Comp.FeelerMinLength);
        var sideLength = CFGOAPSteeringMath.FeelerReach(ent.Comp.FeelerSideLength, speed, ent.Comp.FeelerFullSpeed, ent.Comp.FeelerMinLength);

        var (centre, left, right) = CFGOAPSteeringMath.Fan(heading, ent.Comp.FeelerAngle);
        var ahead = FeelAlong(ent, ref args, centre, length);
        var leftDanger = FeelAlong(ent, ref args, left, sideLength, apply: false);
        var rightDanger = FeelAlong(ent, ref args, right, sideLength, apply: false);

        // Both sides touched with the middle clear is a gap to go through. Their danger would cancel the way forward
        // and hold it in front of an open doorway, and the safety circle still keeps it off the frame.
        if (ahead > 0f || leftDanger <= 0f || rightDanger <= 0f)
        {
            AddDanger(ent, ref args, left, leftDanger);
            AddDanger(ent, ref args, right, rightDanger);
        }

        // Blocked straight ahead, nothing is left with any interest, and a flat wall would hold it there.
        var now = _timing.CurTime;
        if (ahead >= AvoidDanger && now >= ent.Comp.AvoidUntil)
        {
            ent.Comp.AvoidDirection = new Vector2(centre.Y, -centre.X) * CFGOAPSteeringMath.TurnSide(leftDanger, rightDanger);
            ent.Comp.AvoidUntil = now + AvoidTime;
        }

        if (now < ent.Comp.AvoidUntil)
            Add(ref args, ent.Comp.AvoidDirection, AvoidWeight);
    }

    /// <summary>Casts one ray and returns the danger it found, leaving it to the caller when told not to apply it.</summary>
    private float FeelAlong(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args, Vector2 direction, float length, bool apply = true)
    {
        if (!TryFeel(ent, args.WorldPosition, direction, ent.Comp.FeelerSight, out var hit, ent.Comp.Through))
            return 0f;

        if (HasComp<MobStateComponent>(hit.Entity))
            return FeelMob(ent, ref args, hit.Entity);

        // The ray still stops here, so nothing is felt through a wall. It just adds nothing to what the arrows know.
        if (hit.Distance >= length || ent.Comp.OnArrows && IsFixed(hit.Entity) && !HasComp<CFDangerousTileComponent>(hit.Entity))
            return 0f;

        var danger = (1f - hit.Distance / length) * FeelerDanger;
        if (apply)
            AddDanger(ent, ref args, direction, danger);

        return danger;
    }

    private float FeelMob(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args, EntityUid mob)
    {
        ent.Comp.Felt ??= mob;
        var toMob = _transform.GetWorldPosition(mob) - args.WorldPosition;
        if (ent.Comp.FeltMobs == CFGOAPFeltMobs.Chase)
            Add(ref args, toMob, 1f);

        if (ent.Comp.FeltMobs != CFGOAPFeltMobs.Avoid)
            return 0f;

        AddDanger(ent, ref args, toMob, FeelerDanger);
        return FeelerDanger;
    }

    private static void AddDanger(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args, Vector2 direction, float danger)
    {
        if (direction == Vector2.Zero)
            return;

        var norm = Vector2.Normalize(args.OffsetRotation.RotateVec(direction));
        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            var result = CFGOAPSteeringMath.FeelerSpread(Vector2.Dot(norm, NPCSteeringSystem.Directions[i])) * danger;
            if (result > 0f)
                ent.Comp.FeelerTarget[i] = MathF.Max(ent.Comp.FeelerTarget[i], result);
        }
    }

    /// <summary>
    /// Casts one ray and returns the first thing it touches: a wall or other fixed solid, a dangerous tile
    /// (whose fixture is not hard), or an enemy. Allies, and enemies this creature cannot detect, do not stop it.
    /// </summary>
    public bool TryFeel(EntityUid uid, Vector2 from, Vector2 direction, float length, out CFGOAPFeelerHit hit, EntityUid? through = null)
    {
        hit = default;
        var filter = new QueryFilter
        {
            MaskBits = FeelerMask,
            Flags = QueryFlags.Static | QueryFlags.Dynamic | QueryFlags.Sensors,
            IsIgnored = other => other == through || !CanFeel(uid, other),
        };

        var result = _rays.CastRayClosest(Transform(uid).MapID, from, direction * length, filter);
        if (!result.Hit)
            return false;

        var first = result.Results[0];
        hit = new CFGOAPFeelerHit(first.Entity, first.Fraction * length);
        return true;
    }

    private bool IsFixed(EntityUid other)
    {
        return TryComp(other, out PhysicsComponent? body) && body.Hard && body.BodyType == BodyType.Static;
    }

    /// <summary>
    /// Whether the tile underfoot has an arrow. Arrows already go round everything fixed, so feeling for walls as well
    /// only fights them: in a one-tile corridor the middle is 0.5 from each wall, and the safety circle is 0.42 wide.
    /// </summary>
    private bool OnArrows(Entity<CFGOAPSteeringComponent> ent, Vector2 position)
    {
        return ent.Comp is { UsingField: true, Field: { } field }
               && Transform(ent).GridUid is { } grid
               && TryComp(grid, out MapGridComponent? gridComp)
               && field.Arrows.ContainsKey(_maps.WorldToTile(grid, gridComp, position));
    }

    /// <summary>Partway over a frame it has to carry on, not be pushed back the way it came.</summary>
    private bool IsClimbingOver(EntityUid uid, EntityUid other)
    {
        return HasComp<ClimbableComponent>(other) && TryComp(uid, out ClimbingComponent? climbing) && climbing.IsClimbing;
    }

    /// <summary>The sensors query also finds things like triggers, which are nothing to steer around.</summary>
    private bool CanFeel(EntityUid uid, EntityUid other)
    {
        if (other == uid || IsClimbingOver(uid, other))
            return false;

        if (HasComp<MobStateComponent>(other))
        {
            return _mobState.IsAlive(other)
                   && _factions.CanHarm(uid, other)
                   && _stealth.Detection(other, uid) != CFDetection.Undetected;
        }

        if (HasComp<CFDangerousTileComponent>(other))
            return true;

        // Not only what is fixed down: a crate left in the way stops a body just the same.
        return TryComp(other, out PhysicsComponent? body) && body.Hard;
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
    private void OnRefreshSpeed(Entity<CFGOAPSteeringComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = ent.Comp.AppliedSpeed;
        args.ModifySpeed(speed, speed);
    }

    private void ApplySpeed(Entity<CFGOAPSteeringComponent> ent)
    {
        var speed = ent.Comp.SpeedMultiplier * ent.Comp.ArriveSpeed;
        if (Math.Abs(speed - ent.Comp.AppliedSpeed) < SpeedStep)
            return;

        ent.Comp.AppliedSpeed = speed;
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private Vector2 Desired(Entity<CFGOAPSteeringComponent> ent, CFGOAPSteeringBehavior behavior, Vector2 position, Vector2 heading)
    {
        switch (behavior.Kind)
        {
            case CFGOAPSteeringKind.Wander:
                ent.Comp.WanderAngle += _random.NextFloat(-behavior.Jitter, behavior.Jitter);
                return CFGOAPSteeringMath.Wander(heading, ent.Comp.WanderAngle, behavior.Radius);
            case CFGOAPSteeringKind.Cohesion:
                return CFGOAPSteeringMath.Seek(position, AlliesCentre(ent, position, behavior.Radius) ?? position);
            case CFGOAPSteeringKind.Separation:
                return CFGOAPSteeringMath.Flee(position, AlliesCentre(ent, position, behavior.Radius) ?? position);
            case CFGOAPSteeringKind.WallFollow:
                return NearestWall(ent, position, behavior.Radius);
            case CFGOAPSteeringKind.FieldFollow:
                return _influence.TryFollow(ent, behavior.Layer, ent.Comp.Target, out var trail) ? trail : Vector2.Zero;
            case CFGOAPSteeringKind.FlowField:
                return Flow(ent, position);
        }

        if (!TryGetGoal(ent, out var goal, out var velocity))
            return Vector2.Zero;

        var speed = CompOrNull<PhysicsComponent>(ent)?.LinearVelocity.Length() ?? 0f;
        switch (behavior.Kind)
        {
            case CFGOAPSteeringKind.Flee:
                return CFGOAPSteeringMath.Flee(position, goal);
            case CFGOAPSteeringKind.Arrive:
                ent.Comp.ArriveSpeed = CFGOAPSteeringMath.ArriveSpeed(position, goal, behavior.Radius);
                return CFGOAPSteeringMath.Seek(position, goal);
            case CFGOAPSteeringKind.Pursuit:
                return CFGOAPSteeringMath.Pursuit(position, speed, goal, velocity);
            case CFGOAPSteeringKind.Evade:
                return CFGOAPSteeringMath.Evade(position, speed, goal, velocity);
            default:
                return Vector2.Zero;
        }
    }

    /// <summary>
    /// The middle of the tile the arrow underfoot points at. Following the arrow itself would keep a creature's sideways
    /// drift, and in a one-tile corridor that drift ends on a corner. Off the edge of the field it heads straight for
    /// the goal, or from it, so it can get back on.
    /// </summary>
    private Vector2 Flow(Entity<CFGOAPSteeringComponent> ent, Vector2 position)
    {
        var xform = Transform(ent);
        if (ent.Comp.Field is { } field
            && xform.GridUid is { } grid
            && TryComp(grid, out MapGridComponent? gridComp)
            && field.Arrows.TryGetValue(_maps.WorldToTile(grid, gridComp, position), out var arrow))
        {
            if (arrow == Vector2.Zero)
                return Vector2.Zero;

            var next = CFGOAPFlowFieldBuilder.NextTile(_maps.WorldToTile(grid, gridComp, position), arrow);
            return CFGOAPSteeringMath.Seek(position, _maps.GridTileToWorldPos(grid, gridComp, next));
        }

        if (!TryGetGoal(ent, out var goal, out _))
            return Vector2.Zero;

        return ent.Comp.Field is { Away: true }
            ? CFGOAPSteeringMath.Flee(position, goal)
            : CFGOAPSteeringMath.Seek(position, goal);
    }

    private bool TryGetGoal(Entity<CFGOAPSteeringComponent> ent, out Vector2 goal, out Vector2 velocity)
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
