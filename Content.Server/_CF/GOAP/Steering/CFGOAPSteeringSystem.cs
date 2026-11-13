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
using Content.Shared.Movement.Components;
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

    /// <summary>The furthest ahead, in seconds, Arrive aims for a moving creature.</summary>
    private const float ApproachLead = 0.4f;

    private const float MinimumEscape = 0.0001f;

    /// <summary>The search has to reach as far as the widest circle speed can give.</summary>
    private const float PanicLeadSearch = 0.2f;

    /// <summary>Well above anything else steering writes, so it wins outright.</summary>
    private const float PanicForce = 4f;

    /// <summary>Added to the search, because a tile's center is further away than its edge.</summary>
    private const float PanicSearchMargin = 0.75f;

    /// <summary>
    /// The feelers and the safety circle look every this many steering ticks, 0.1 seconds at 30 a second. Between
    /// looks the safety circle still measures every tick, against what the last look found.
    /// </summary>
    private const int FeelEvery = 3;

    /// <summary>Turning further than 30 degrees from where the feelers last looked makes them look again.</summary>
    private static readonly float LookTurn = MathF.Cos(MathF.PI / 6f);

    /// <summary>
    /// How much further than the search it keeps fixed things, so it can walk this far before looking again.
    /// </summary>
    private const float PanicStaticMargin = 1.5f;

    /// <summary>How far a calm creature may move in one tick, on top of its feelers' reach, for open floor to be sure.</summary>
    private const float OpenFloorSlack = 0.25f;

    /// <summary>Fixed things are looked for again at least this often, in case a door shut or a wall went up.</summary>
    private static readonly TimeSpan PanicFixedRefresh = TimeSpan.FromSeconds(0.5);

    /// <summary>How often the fan casts its long rays, which only spot enemies further off.</summary>
    private static readonly TimeSpan SightInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Two mobs closing on each other at a run between looks, with room to spare.</summary>
    private const float PanicMobMargin = 1.5f;

    /// <summary>
    /// How much further the search for mobs reaches, so a lookup by position cannot miss a body whose centre is just
    /// outside it. The safety circle measures to their hard bodies, never the 2.5 tile flanking sensor.
    /// </summary>
    private const float PanicMobBounds = 2.5f;

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

    // Scratch space, cleared before each use and never read after it, so nothing about a creature is kept here.
    // Reusing these keeps steering from allocating every tick.
    private readonly HashSet<EntityUid> _panicFound = new();
    private readonly HashSet<Entity<MobStateComponent>> _mobsFound = new();
    private EntityUid _feelUid;
    private EntityUid? _feelThrough;
    private Func<EntityUid, bool> _feelIgnored = default!;

    public override void Initialize()
    {
        base.Initialize();

        // One delegate for every ray. A lambda per ray allocated each time.
        _feelIgnored = IsFeelIgnored;
    }

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
        steering.PointVelocity = Vector2.Zero;
        steering.Settled = false;
        steering.FeelCountdown = 0;
        steering.FeelForced = true;

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
        ent.Comp.PointVelocity = Vector2.Zero;
        ent.Comp.Settled = false;
        ent.Comp.FeelCountdown = 0;
        ent.Comp.FeelForced = true;
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

            // Arrive comes first and has stopped it. Habits such as keeping to walls would only nudge it off the spot,
            // and then it walks back: that back and forth is the twitch.
            if (ent.Comp.Settled && IsHabit(behavior.Kind))
                continue;

            Add(ref args, Desired(ent, behavior, args.WorldPosition, args.Steering.LastSteerDirection), behavior.Weight);
        }

        if (ent.Comp.Behaviors.Count > 0 && !usesStockSeek)
            args.Steering.CanSeek = false;

        // Stock keeps last tick's interest and only eases it toward zero, and any interest at all is a step at full
        // input. Without clearing it, a creature that has arrived keeps walking on for a moment and overshoots.
        if (ent.Comp.Settled)
            Array.Clear(args.Steering.Interest);

        Feel(ent, ref args);
        ApplySpeed(ent);
    }

    /// <summary>
    /// A three-ray fan along the way it is heading. Stock takes danger away from interest, so a feeler touching
    /// something turns every kind of movement aside. A ray that reaches an enemy adds to the vectors instead.
    /// Rays reach further at speed, which makes their danger work like time to impact.
    /// The rays only look every <see cref="FeelEvery"/> ticks, or sooner on a sharp turn or near a dangerous tile.
    /// What they found holds in between, and the interest they add is added again each tick.
    /// </summary>
    private void Feel(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args)
    {
        ent.Comp.OnArrows = OnArrows(ent, args.WorldPosition);
        var heading = Heading(ent, ref args);
        var look = ShouldLook(ent.Comp, heading, out var forced);
        var now = _timing.CurTime;

        // Long rays only spot enemies further off, which the sight sensor reads twice a second, so they wait longer.
        // A sharp turn or new behaviours cast them at once, or what they saw would be the wrong way.
        var sweep = look && !ent.Comp.Calm && (forced || now >= ent.Comp.NextSight);
        if (sweep)
            ent.Comp.NextSight = now + SightInterval;

        if (look)
        {
            ent.Comp.FeelForced = false;
            ent.Comp.FeelCountdown = FeelEvery - 1;
            ent.Comp.FeltHeading = heading;
            ent.Comp.Felt = null;
            ent.Comp.FanMobs.Clear();
            Array.Clear(ent.Comp.FeelerTarget);

            // Calm, it leaves spotting enemies to its sight sensor, so nothing far off is kept.
            if (ent.Comp.Calm)
                Array.Clear(ent.Comp.SightMobs);
        }
        else
        {
            ent.Comp.FeelCountdown--;
        }

        // Beside or behind it, where the fan cannot reach. Flanking already tracks these, so this is cheap every tick.
        foreach (var enemy in _flanking.EnemiesInReach(ent))
        {
            if (CanFeel(ent, enemy))
                FeelMob(ent, ref args, enemy);
        }

        if (look)
        {
            // Calm on open floor there is nothing for the rays to find.
            if (heading != Vector2.Zero && !(ent.Comp.Calm && IsOpenFloor(ent, args.WorldPosition)))
                FeelFan(ent, ref args, heading, sweep);
        }
        else
        {
            foreach (var mob in ent.Comp.FanMobs)
            {
                if (!TerminatingOrDeleted(mob))
                    FeelMob(ent, ref args, mob);
            }
        }

        // The way it committed to going round something, until that runs out.
        if (now < ent.Comp.AvoidUntil)
            Add(ref args, ent.Comp.AvoidDirection, AvoidWeight);

        var frameTime = (float) _timing.FrameTime.TotalSeconds;
        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            ent.Comp.FeelerDanger[i] = CFGOAPSteeringMath.SmoothDanger(ent.Comp.FeelerDanger[i], ent.Comp.FeelerTarget[i], frameTime);
            args.Steering.Danger[i] = MathF.Max(args.Steering.Danger[i], ent.Comp.FeelerDanger[i]);
        }

        Panic(ent, ref args, look);
    }

    private static bool IsHabit(CFGOAPSteeringKind kind)
    {
        return kind is CFGOAPSteeringKind.WallFollow or CFGOAPSteeringKind.Separation or CFGOAPSteeringKind.Cohesion;
    }

    /// <summary>Whether the feelers and the safety circle look again this tick.</summary>
    private static bool ShouldLook(CFGOAPSteeringComponent steering, Vector2 heading, out bool forced)
    {
        var last = steering.FeltHeading;
        forced = steering.FeelForced
                 || (heading == Vector2.Zero || last == Vector2.Zero
                     ? heading != last
                     : Vector2.Dot(Vector2.Normalize(heading), Vector2.Normalize(last)) < LookTurn);

        return forced || steering.FeelCountdown <= 0 || steering.NearDanger;
    }

    /// <summary>
    /// The safety circle. Rays run ahead, so a creature sliding alongside lava can miss all three while its body
    /// clips the tile. Anything inside the circle overrides everything else, with no easing, and snaps it away.
    /// </summary>
    private void Panic(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args, bool look)
    {
        var position = args.WorldPosition;
        var velocity = CompOrNull<PhysicsComponent>(ent)?.LinearVelocity ?? Vector2.Zero;
        var escape = Vector2.Zero;
        var onlyWalls = true;
        var search = ent.Comp.PanicRadius + velocity.Length() * PanicLeadSearch + PanicSearchMargin;
        if (look)
            FindPanicMobs(ent, position, search);

        PanicFromFixed(ent, position, velocity, search, ref escape, ref onlyWalls);

        foreach (var other in ent.Comp.PanicMobs)
        {
            if (TerminatingOrDeleted(other) || !IsPanicCause(ent, other))
                continue;

            // Its body, not its whole bounds: the flanking sensor makes those 2.5 tiles wide, which held every creature
            // that far off its master and pack mates, and set it stepping back and forth at that distance.
            var closest = CFGOAPSteeringMath.ClosestPoint(_physics.GetHardAABB(other), position);
            var away = position - closest;
            if (away == Vector2.Zero)
                away = position - _transform.GetWorldPosition(other);

            var distance = away.Length();
            if (distance > 0f && distance < ent.Comp.PanicRadius)
            {
                escape += away / distance * (ent.Comp.PanicRadius - distance);
                onlyWalls = false;
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

    /// <summary>
    /// Walls, fixed solids and dangerous tiles push it away as mobs do, but they do not move on their grid. Their
    /// bounds are kept on the grid and looked for again only once it has walked about a tile, so each tick is plain
    /// math. A door that opened, or anything destroyed, is skipped by checking its body.
    /// </summary>
    private void PanicFromFixed(Entity<CFGOAPSteeringComponent> ent, Vector2 position, Vector2 velocity, float search, ref Vector2 escape, ref bool onlyWalls)
    {
        if (Transform(ent).GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
        {
            ent.Comp.PanicFixed.Clear();
            ent.Comp.PanicGrid = null;
            ent.Comp.NearDanger = false;
            return;
        }

        // Calm, it also has to know the feelers' reach, to tell open floor.
        var local = _maps.WorldToLocal(grid, gridComp, position);
        var need = ent.Comp.Calm ? MathF.Max(search, FeelerReach(ent.Comp) + OpenFloorSlack) : search;
        if (ent.Comp.PanicGrid != grid
            || _timing.CurTime >= ent.Comp.PanicRefreshAt
            || (local - ent.Comp.PanicCentre).Length() + need > ent.Comp.PanicRange)
        {
            FindPanicFixed(ent, grid, local, need);
        }

        // Busy with an obstacle, it stays put. What stands beside it cannot hurt it, and being thrown clear of the next
        // table along would only end the climb, over and over. Dangerous tiles still count.
        var wallsCount = ent.Comp.Through == null && !ent.Comp.OnArrows;
        var climbing = TryComp(ent, out ClimbingComponent? climb) && climb.IsClimbing;
        var toWorld = _transform.GetWorldRotation(grid);
        foreach (var other in ent.Comp.PanicFixed)
        {
            if (other.Uid == ent.Comp.Target || other.Uid == ent.Comp.Through || climbing && other.Climbable)
                continue;

            if (other.Dangerous)
            {
                if (TerminatingOrDeleted(other.Uid))
                    continue;
            }
            else if (!wallsCount || other.Body is not { Hard: true, CanCollide: true } body || body.LifeStage > ComponentLifeStage.Running)
            {
                continue;
            }

            var awayLocal = local - CFGOAPSteeringMath.ClosestPoint(other.Bounds, local);

            // Already overlapping, as a mob on a lava tile would be. Its center is the way out.
            if (awayLocal == Vector2.Zero)
                awayLocal = local - other.Bounds.Center;

            var away = toWorld.RotateVec(awayLocal);

            // A wall only stops it, so it can come close. Lava it has to turn away from in time.
            var radius = other.Dangerous
                ? CFGOAPSteeringMath.HazardRadius(ent.Comp.PanicRadius, velocity, -away)
                : ent.Comp.PanicRadius;

            var distance = away.Length();
            if (distance > 0f && distance < radius)
            {
                escape += away / distance * (radius - distance);
                onlyWalls &= !other.Dangerous;
            }
        }
    }

    /// <summary>
    /// Finds the fixed things near it on the grid. Static fixtures only: sensors among them are dangerous tiles, which
    /// are not hard. Creatures' 2.5 tile flanking sensors are on moving bodies, so this never touches them.
    /// </summary>
    private void FindPanicFixed(Entity<CFGOAPSteeringComponent> ent, EntityUid grid, Vector2 local, float need)
    {
        var range = need + PanicStaticMargin;
        ent.Comp.PanicFixed.Clear();
        ent.Comp.PanicGrid = grid;
        ent.Comp.PanicCentre = local;
        ent.Comp.PanicRange = range;
        ent.Comp.PanicRefreshAt = _timing.CurTime + PanicFixedRefresh;
        ent.Comp.NearDanger = false;

        _panicFound.Clear();
        var area = new Box2(local - new Vector2(range), local + new Vector2(range));
        _lookup.GetLocalEntitiesIntersecting(grid, area, _panicFound, LookupFlags.Static | LookupFlags.Sensors);
        foreach (var other in _panicFound)
        {
            var dangerous = HasComp<CFDangerousTileComponent>(other);
            if (!TryComp(other, out PhysicsComponent? body) || !dangerous && !(body.Hard && body.BodyType == BodyType.Static))
                continue;

            // Bounds on the grid, which never change while it is anchored there.
            var xform = Transform(other);
            if (xform.ParentUid != grid)
                continue;

            var bounds = _lookup.GetAABBNoContainer(other, xform.LocalPosition, xform.LocalRotation);
            if ((local - CFGOAPSteeringMath.ClosestPoint(bounds, local)).Length() > range)
                continue;

            ent.Comp.PanicFixed.Add(new CFGOAPPanicFixed(other, dangerous ? null : body, bounds, dangerous, HasComp<ClimbableComponent>(other)));
            ent.Comp.NearDanger |= dangerous;
        }

        // Only a calm creature reads this, so only it pays for the search.
        ent.Comp.LooseObstacleNear = false;
        if (!ent.Comp.Calm)
            return;

        _panicFound.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, area, _panicFound, LookupFlags.Dynamic);
        var mask = _physics.GetHardCollision(ent).Mask;
        foreach (var other in _panicFound)
        {
            if (other == ent.Owner || HasComp<MobStateComponent>(other))
                continue;

            if ((_physics.GetHardCollision(other).Layer & mask) != 0)
            {
                ent.Comp.LooseObstacleNear = true;
                return;
            }
        }
    }

    /// <summary>The furthest its feelers reach, in tiles.</summary>
    private static float FeelerReach(CFGOAPSteeringComponent steering)
    {
        return MathF.Max(steering.FeelerLength, steering.FeelerSideLength);
    }

    /// <summary>
    /// Nothing within its feelers' reach that they could find: no wall, fixed solid, dangerous tile or loose obstacle.
    /// Only sure inside the area the last look for fixed things covered. Other mobs do not count, because a calm
    /// creature ignores them and the safety circle still keeps it off them.
    /// </summary>
    private bool IsOpenFloor(Entity<CFGOAPSteeringComponent> ent, Vector2 position)
    {
        if (ent.Comp.NearDanger
            || ent.Comp.LooseObstacleNear
            || ent.Comp.Through != null
            || ent.Comp.PanicGrid is not { } grid
            || Transform(ent).GridUid != grid
            || !TryComp(grid, out MapGridComponent? gridComp))
            return false;

        var reach = FeelerReach(ent.Comp);
        var local = _maps.WorldToLocal(grid, gridComp, position);
        if (ent.Comp.PanicRange - (local - ent.Comp.PanicCentre).Length() < reach + OpenFloorSlack)
            return false;

        foreach (var other in ent.Comp.PanicFixed)
        {
            if (other.Body is { Hard: true, CanCollide: true }
                && (local - CFGOAPSteeringMath.ClosestPoint(other.Bounds, local)).Length() < reach)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Finds mobs that could come within the search before the next look. They are found by their bodies, not by
    /// searching sensors, which would find every creature's flanking sensor and made crowds slow.
    /// </summary>
    private void FindPanicMobs(Entity<CFGOAPSteeringComponent> ent, Vector2 position, float search)
    {
        ent.Comp.PanicMobs.Clear();
        var range = search + PanicMobMargin;
        _mobsFound.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).MapID, position, range + PanicMobBounds, _mobsFound, LookupFlags.Dynamic);
        foreach (var mob in _mobsFound)
        {
            if (mob.Owner == ent.Owner)
                continue;

            var closest = CFGOAPSteeringMath.ClosestPoint(_physics.GetHardAABB(mob), position);
            if ((position - closest).LengthSquared() <= range * range)
                ent.Comp.PanicMobs.Add(mob.Owner);
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

    private void FeelFan(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args, Vector2 heading, bool sweep)
    {
        var speed = CompOrNull<PhysicsComponent>(ent)?.LinearVelocity.Length() ?? 0f;
        var length = CFGOAPSteeringMath.FeelerReach(ent.Comp.FeelerLength, speed, ent.Comp.FeelerFullSpeed, ent.Comp.FeelerMinLength);
        var sideLength = CFGOAPSteeringMath.FeelerReach(ent.Comp.FeelerSideLength, speed, ent.Comp.FeelerFullSpeed, ent.Comp.FeelerMinLength);

        var (centre, left, right) = CFGOAPSteeringMath.Fan(heading, ent.Comp.FeelerAngle);
        var ahead = FeelAlong(ent, ref args, centre, length, 0, sweep);
        var leftDanger = FeelAlong(ent, ref args, left, sideLength, 1, sweep, apply: false);
        var rightDanger = FeelAlong(ent, ref args, right, sideLength, 2, sweep, apply: false);

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
    }

    /// <summary>
    /// Casts one ray and returns the danger it found, leaving it to the caller when told not to apply it. The ray only
    /// reaches the feeler's length. Past that it can only spot an enemy, which a long ray looks for when
    /// <paramref name="sweep"/> is set; what it saw counts until the next sweep.
    /// </summary>
    private float FeelAlong(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args, Vector2 direction, float length, int ray, bool sweep, bool apply = true)
    {
        if (TryFeel(ent, args.WorldPosition, direction, length, out var hit, ent.Comp.Through))
        {
            if (HasComp<MobStateComponent>(hit.Entity))
            {
                ent.Comp.FanMobs.Add(hit.Entity);
                return FeelMob(ent, ref args, hit.Entity);
            }

            // What is fixed is left to the arrows, which already go round it.
            if (ent.Comp.OnArrows && IsFixed(hit.Entity) && !HasComp<CFDangerousTileComponent>(hit.Entity))
                return 0f;

            var danger = (1f - hit.Distance / length) * FeelerDanger;
            if (apply)
                AddDanger(ent, ref args, direction, danger);

            return danger;
        }

        // Nothing close. The ray still stops at a wall, so nothing is spotted through one.
        if (sweep)
        {
            ent.Comp.SightMobs[ray] = TryFeel(ent, args.WorldPosition, direction, ent.Comp.FeelerSight, out var far, ent.Comp.Through)
                                      && HasComp<MobStateComponent>(far.Entity)
                ? far.Entity
                : null;
        }

        if (ent.Comp.SightMobs[ray] is not { } seen || TerminatingOrDeleted(seen) || !CanFeel(ent, seen))
            return 0f;

        ent.Comp.FanMobs.Add(seen);
        return FeelMob(ent, ref args, seen);
    }

    private float FeelMob(Entity<CFGOAPSteeringComponent> ent, ref NPCSteeringEvent args, EntityUid mob)
    {
        ent.Comp.Felt ??= mob;
        var toMob = _transform.GetWorldPosition(mob) - args.WorldPosition;

        // The creature it is after already has a behaviour that slows down and stops beside it. Full interest here
        // as well would drive it on into their body.
        if (ent.Comp.FeltMobs == CFGOAPFeltMobs.Chase && mob != ent.Comp.Target)
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

        // Sensors only so it touches dangerous tiles, whose fixtures are not hard. Flanking sensors are on no layer,
        // so the mask already keeps rays off them.
        _feelUid = uid;
        _feelThrough = through;
        var filter = new QueryFilter
        {
            MaskBits = FeelerMask,
            Flags = QueryFlags.Static | QueryFlags.Dynamic | QueryFlags.Sensors,
            IsIgnored = _feelIgnored,
        };

        var result = _rays.CastRayClosest(Transform(uid).MapID, from, direction * length, filter);
        if (!result.Hit)
            return false;

        var first = result.Results[0];
        hit = new CFGOAPFeelerHit(first.Entity, first.Fraction * length);
        return true;
    }

    private bool IsFeelIgnored(EntityUid other)
    {
        return other == _feelThrough || !CanFeel(_feelUid, other);
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
                return Approach(ent, behavior, position, goal, velocity);
            case CFGOAPSteeringKind.Pursuit:
                return CFGOAPSteeringMath.Pursuit(position, speed, goal, velocity);
            case CFGOAPSteeringKind.Evade:
                return CFGOAPSteeringMath.Evade(position, speed, goal, velocity);
            default:
                return Vector2.Zero;
        }
    }

    /// <summary>
    /// Arrive: heads for the goal, slows down over the last stretch, and stops short of it. A creature is aimed at
    /// where it will be in a moment, so following one that walks off does not lag behind.
    /// </summary>
    private Vector2 Approach(Entity<CFGOAPSteeringComponent> ent, CFGOAPSteeringBehavior behavior, Vector2 position, Vector2 goal, Vector2 velocity)
    {
        var maxSpeed = MaxSpeed(ent);
        if (velocity != Vector2.Zero && maxSpeed > 0f)
            goal += velocity * MathF.Min(Vector2.Distance(position, goal) / maxSpeed, ApproachLead);

        var settled = ent.Comp.Settled;
        var share = CFGOAPSteeringMath.ApproachSpeed(
            position,
            goal,
            velocity,
            maxSpeed,
            behavior.Radius,
            behavior.Standoff,
            behavior.Resume,
            ref settled);

        ent.Comp.Settled = settled;
        if (share <= 0f)
            return Vector2.Zero;

        ent.Comp.ArriveSpeed = share;
        return CFGOAPSteeringMath.Seek(position, goal);
    }

    /// <summary>Its top speed before what steering itself takes off, in tiles a second.</summary>
    private float MaxSpeed(Entity<CFGOAPSteeringComponent> ent)
    {
        if (!TryComp(ent, out MovementSpeedModifierComponent? modifier) || ent.Comp.AppliedSpeed <= 0f)
            return 0f;

        return modifier.CurrentSprintSpeed / ent.Comp.AppliedSpeed * ent.Comp.SpeedMultiplier;
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
        velocity = ent.Comp.PointVelocity;
        return ent.Comp.Point != null;
    }

    private Vector2? AlliesCentre(EntityUid uid, Vector2 position, float radius)
    {
        var total = Vector2.Zero;
        var count = 0;
        _mobsFound.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(uid), radius, _mobsFound);
        foreach (var mob in _mobsFound)
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
