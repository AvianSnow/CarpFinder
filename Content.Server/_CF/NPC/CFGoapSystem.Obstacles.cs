using System.Numerics;
using Content.Server.Destructible;
using Content.Server.NPC.Components;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.CF.NPC;

/// <summary>What something standing in the way means to a creature.</summary>
public enum CFBlocker : byte
{
    /// <summary>Nothing that stops it.</summary>
    None,

    /// <summary>A door it can open.</summary>
    Door,

    /// <summary>A locked door or a window. It has to break through.</summary>
    Smash,

    /// <summary>Low enough to climb over, as a broken window frame is.</summary>
    Climb,

    /// <summary>A wall, or anything else it cannot do anything about.</summary>
    Solid,
}

/// <summary>
/// Doors and glass. Stock handles these from its path code, and GOAP has no paths, so a flow field counts them as slow
/// tiles and the creature deals with them when an arrow leads it into one.
/// </summary>
public sealed partial class CFGoapSystem
{
    /// <summary>How far along its arrow it notices what it will have to get through, in tiles.</summary>
    private const float ObstacleSight = 2.5f;

    // Extra cost of crossing a tile, where a plain tile costs 10. A detour shorter than this wins.
    private const int DoorCost = 10;
    private const int GlassCost = 60;
    private const int LockedDoorCost = 100;

    /// <summary>How long a door it gave up on stays off its map.</summary>
    private static readonly TimeSpan GiveUpMemory = TimeSpan.FromSeconds(60);

    [Dependency] private ClimbSystem _climb = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    private CFBlocker Classify(EntityUid uid, EntityUid other)
    {
        if (TryComp(other, out DoorComponent? door))
        {
            if (door.State is DoorState.Open or DoorState.Opening)
                return CFBlocker.None;

            if (HasComp<HandsComponent>(uid) && _doors.CanOpen(other, door, uid))
                return CFBlocker.Door;

            return HasComp<DestructibleComponent>(other) ? CFBlocker.Smash : CFBlocker.Solid;
        }

        var layer = (CollisionGroup) _physics.GetHardCollision(other).Layer;
        if ((layer & CollisionGroup.Impassable) == 0)
        {
            // What is left of a smashed door is not Impassable, but it stops a body all the same.
            if ((layer & CollisionGroup.GlassAirlockLayer) == CollisionGroup.GlassAirlockLayer)
                return HasComp<DestructibleComponent>(other) ? CFBlocker.Smash : CFBlocker.Solid;

            if (!HasComp<ClimbableComponent>(other) || !TryComp(uid, out ClimbingComponent? climbing))
                return CFBlocker.None;

            return climbing.IsClimbing ? CFBlocker.None : CFBlocker.Climb;
        }

        // A wall has the same layer as glass, plus Opaque.
        var glass = (layer & CollisionGroup.GlassLayer) == CollisionGroup.GlassLayer && (layer & CollisionGroup.Opaque) == 0;
        return glass && HasComp<DestructibleComponent>(other) ? CFBlocker.Smash : CFBlocker.Solid;
    }

    private bool HasGivenUp(CFFlowField field, EntityUid obstacle)
    {
        return field.GaveUp.TryGetValue(obstacle, out var until) && _timing.CurTime < until;
    }

    /// <summary>What crossing a tile adds to a field. Dangerous tiles are blocked, so the arrows lead around lava.</summary>
    private int TileCost(EntityUid uid, CFFlowField field, EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        if (_steering.IsDangerous(_maps.GridTileToWorld(grid, gridComp, tile)))
            return CFFlowFieldBuilder.Blocked;

        if (!_turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable))
            return 0;

        var cost = CFFlowFieldBuilder.Blocked;
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (anchored.MoveNext(out var found))
        {
            var other = found.Value;
            switch (Classify(uid, other))
            {
                case CFBlocker.Solid:
                    return CFFlowFieldBuilder.Blocked;
                case CFBlocker.Door when !HasGivenUp(field, other):
                    cost = Math.Max(cost, DoorCost);
                    break;
                case CFBlocker.Smash when !HasGivenUp(field, other):
                    cost = Math.Max(cost, HasComp<DoorComponent>(other) ? LockedDoorCost : GlassCost);
                    break;
                case CFBlocker.Door:
                case CFBlocker.Smash:
                    return CFFlowFieldBuilder.Blocked;
            }
        }

        return cost;
    }

    /// <summary>The arrow under its feet, in world space.</summary>
    private bool TryGetArrow(EntityUid uid, CFFlowField field, out Vector2 arrow)
    {
        arrow = default;
        if (Transform(uid).GridUid is not { } grid
            || !TryComp(grid, out MapGridComponent? gridComp)
            || !field.Arrows.TryGetValue(_maps.WorldToTile(grid, gridComp, _transform.GetWorldPosition(uid)), out var local))
            return false;

        arrow = _transform.GetWorldRotation(grid).RotateVec(local);
        return true;
    }

    /// <summary>Whether its arrow leads into something it has to open or break, and if so goes up to it.</summary>
    private void LookForObstacle(Entity<CFGoapComponent> ent, CFFlowField field)
    {
        if (!TryGetArrow(ent, field, out var arrow)
            || !_steering.TryFeel(ent, _transform.GetWorldPosition(ent), arrow, ObstacleSight, out var hit)
            || Classify(ent, hit.Entity) is CFBlocker.None or CFBlocker.Solid
            || HasGivenUp(field, hit.Entity)
            || !IsOnRoute(ent, field, hit.Entity))
            return;

        ent.Comp.Obstacle = hit.Entity;
        ent.Comp.ObstacleStart = _timing.CurTime;
        WalkUpTo(ent, hit.Entity);
    }

    private void WalkUpTo(Entity<CFGoapComponent> ent, EntityUid obstacle)
    {
        var profile = Profile(ent);
        var arrive = new CFSteeringBehavior { Kind = CFSteeringKind.Arrive, Radius = profile.ArriveRadius };
        _steering.Steer(ent, Steer(profile, arrive), point: _transform.GetWorldPosition(obstacle), felt: CFFeltMobs.Chase, through: obstacle);
    }

    /// <summary>Whether the arrows really lead into it. They may turn off just before something straight ahead.</summary>
    private bool IsOnRoute(EntityUid uid, CFFlowField field, EntityUid obstacle)
    {
        if (Transform(uid).GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return false;

        var goal = _maps.WorldToTile(grid, gridComp, _transform.GetWorldPosition(obstacle));
        var tile = _maps.WorldToTile(grid, gridComp, _transform.GetWorldPosition(uid));
        for (var i = 0; i < ObstacleSight + 1 && field.Arrows.TryGetValue(tile, out var arrow); i++)
        {
            tile += new Vector2i((int) MathF.Round(arrow.X), (int) MathF.Round(arrow.Y));
            if (tile == goal)
                return true;
        }

        return false;
    }

    /// <summary>Opens or breaks what is in the way. Returns false once that is over and it should move on.</summary>
    private bool WorkOnObstacle(Entity<CFGoapComponent> ent, Vector2 goal)
    {
        var obstacle = ent.Comp.Obstacle!.Value;
        var kind = TerminatingOrDeleted(obstacle) ? CFBlocker.None : Classify(ent, obstacle);
        if (kind == CFBlocker.None || CanWalkStraight(ent, goal))
        {
            ClearObstacle(ent);
            return false;
        }

        // A personality only has so much patience for this. Mates sharing the field give up with it.
        var field = FieldOf(ent);
        var patience = TimeSpan.FromSeconds(Profile(ent).SmashPatience);
        if (kind == CFBlocker.Solid || HasGivenUp(field, obstacle) || _timing.CurTime - ent.Comp.ObstacleStart >= patience)
        {
            GiveUp(ent, field, obstacle, goal);
            return false;
        }

        // From the middle, as a swing measures. InRangeUnobstructed goes surface to surface, which says yes too early.
        if ((_transform.GetWorldPosition(obstacle) - _transform.GetWorldPosition(ent)).Length() > ReachTiles)
        {
            // Tripped or pushed away from it mid-work, it has to walk back.
            if (ent.Comp.Working)
            {
                ent.Comp.Working = false;
                RemComp<NPCMeleeCombatComponent>(ent);
                WalkUpTo(ent, obstacle);
            }

            return true;
        }

        if (kind == CFBlocker.Door)
        {
            _doors.TryOpen(obstacle, user: ent);
            return true;
        }

        if (!ent.Comp.Working)
        {
            // Both stay put while they work, and moving would interrupt a climb.
            ent.Comp.Working = true;
            _steering.Steer(ent, new List<CFSteeringBehavior> { new() { Kind = CFSteeringKind.Hold } }, through: obstacle);
            if (kind == CFBlocker.Smash)
            {
                _combatMode.SetInCombatMode(ent, true);
                EnsureComp<NPCMeleeCombatComponent>(ent).Target = obstacle;
            }
        }

        // Glass underfoot can trip it, so it keeps trying until it gets up and over.
        if (kind == CFBlocker.Climb
            && !_conditions.IsProne(ent)
            && TryComp(ent, out ClimbingComponent? climbing)
            && climbing.DoAfter == null)
            _climb.TryClimb(ent, ent, obstacle, out _, climbing: climbing);

        return true;
    }

    private void ClearObstacle(Entity<CFGoapComponent> ent)
    {
        ent.Comp.Obstacle = null;
        ent.Comp.Working = false;
        ent.Comp.Rerouted = true;
        RemComp<NPCMeleeCombatComponent>(ent);
    }

    /// <summary>Takes it off the map and looks for another way. With none, it stops trying for a while.</summary>
    private void GiveUp(Entity<CFGoapComponent> ent, CFFlowField field, EntityUid obstacle, Vector2 goal)
    {
        var until = _timing.CurTime + GiveUpMemory;
        field.GaveUp[obstacle] = until;
        ClearObstacle(ent);
        BuildField(ent, field, goal);
        if (!TryGetArrow(ent, field, out _))
            ent.Comp.NoWayUntil = until;
    }
}
