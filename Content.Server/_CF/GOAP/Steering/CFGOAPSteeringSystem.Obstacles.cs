using System.Numerics;
using Content.Server.Destructible;
using Content.Server.NPC.Components;
using Content.Shared.CF.Combat;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;

namespace Content.Server.CF.GOAP.Steering;

/// <summary>What something standing in the way means to a creature.</summary>
public enum CFGOAPBlocker : byte
{
    /// <summary>Nothing that stops it.</summary>
    None,

    /// <summary>A door it can open.</summary>
    Door,

    /// <summary>In the way, and it can break it: a locked door, a window, a barrel.</summary>
    Smash,

    /// <summary>Low enough to climb over, as a table or a broken window frame is.</summary>
    Climb,

    /// <summary>A wall, or anything else it cannot do anything about. It goes around.</summary>
    Solid,
}

/// <summary>
/// Everything in the way that is not a wall: doors, glass, tables, crates, barrels. Stock handles these from its path code,
/// and GOAP has no paths, so a flow field counts them as slow tiles and the creature deals with them when an arrow leads
/// it into one. What it cannot deal with is blocked, so it goes around.
/// </summary>
public sealed partial class CFGOAPSteeringSystem
{
    /// <summary>How far along its arrow it notices what it will have to get through, in tiles.</summary>
    private const float ObstacleSight = 2.5f;

    /// <summary>How close it has to be to swing at an obstacle, in tiles.</summary>
    private const float ReachTiles = 1.25f;

    // Extra cost of crossing a tile, where a plain tile costs 10. A detour shorter than this wins.
    private const int DoorCost = 10;
    private const int ClimbCost = 30;
    private const int GlassCost = 60;
    private const int LockedDoorCost = 100;

    /// <summary>How long a door it gave up on stays off its map.</summary>
    private static readonly TimeSpan GiveUpMemory = TimeSpan.FromSeconds(60);

    [Dependency] private ClimbSystem _climb = default!;
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private CFCombatBridgeSystem _bridge = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;

    /// <summary>Scratch space for looking at one tile.</summary>
    private readonly HashSet<EntityUid> _tileContents = new();

    /// <summary>
    /// What something on a tile means to this creature. Anything that does not stop its body is nothing to it, such as a
    /// rug or a chair. Creatures are left to the feelers, because they move.
    /// </summary>
    private CFGOAPBlocker Classify(EntityUid uid, EntityUid other)
    {
        if (TryComp(other, out DoorComponent? door))
        {
            if (door.State is DoorState.Open or DoorState.Opening)
                return CFGOAPBlocker.None;

            if (HasComp<HandsComponent>(uid) && _doors.CanOpen(other, door, uid))
                return CFGOAPBlocker.Door;

            return CanBreak(uid, other) ? CFGOAPBlocker.Smash : CFGOAPBlocker.Solid;
        }

        if (HasComp<MobStateComponent>(other))
            return CFGOAPBlocker.None;

        var layer = (CollisionGroup) _physics.GetHardCollision(other).Layer;
        if ((layer & (CollisionGroup) _physics.GetHardCollision(uid).Mask) == 0)
            return CFGOAPBlocker.None;

        // A wall has the same layer as glass, plus Opaque, and nothing here is going to break one.
        if ((layer & CollisionGroup.Impassable) != 0 && (layer & CollisionGroup.Opaque) != 0)
            return CFGOAPBlocker.Solid;

        if (HasComp<ClimbableComponent>(other) && TryComp(uid, out ClimbingComponent? climbing))
            return climbing.IsClimbing ? CFGOAPBlocker.None : CFGOAPBlocker.Climb;

        return CanBreak(uid, other) ? CFGOAPBlocker.Smash : CFGOAPBlocker.Solid;
    }

    /// <summary>
    /// Whether its plain hit gets past the object's Hardness. Without that, smashing is only standing there swinging: a
    /// grille has Hardness 16 and a longsword averages about 8. Something with no stats to compare gets to try.
    /// </summary>
    private bool CanBreak(EntityUid uid, EntityUid other)
    {
        if (!HasComp<DestructibleComponent>(other) || !_bridge.TryEnsureCombatTarget(other, out _, out var hardness, out _))
            return false;

        return !_strikes.TryPreviewMelee(uid, other, out var strike) || strike.AverageDamage > hardness;
    }

    private bool HasGivenUp(CFGOAPFlowField field, EntityUid obstacle)
    {
        return field.GaveUp.TryGetValue(obstacle, out var until) && _timing.CurTime < until;
    }

    /// <summary>What crossing a tile adds to a field. Dangerous tiles are blocked, so the arrows lead around lava.</summary>
    private int TileCost(EntityUid uid, CFGOAPFlowField field, EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        // Past the edge of the island there is nothing to stand on.
        if (!_maps.TryGetTileRef(grid, gridComp, tile, out var tileRef)
            || tileRef.Tile.IsEmpty
            || IsDangerous(_maps.GridTileToWorld(grid, gridComp, tile)))
            return CFGOAPFlowFieldBuilder.Blocked;

        // Everything hard on the tile, not just what is Impassable: tables are not, and a crate is not even anchored.
        _tileContents.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, tile, _tileContents, flags: LookupFlags.Dynamic | LookupFlags.Static, gridComp: gridComp);

        var cost = 0;
        foreach (var other in _tileContents)
        {
            var extra = 0;
            switch (Classify(uid, other))
            {
                case CFGOAPBlocker.None:
                    continue;
                case CFGOAPBlocker.Solid:
                    return CFGOAPFlowFieldBuilder.Blocked;
                case CFGOAPBlocker.Door:
                    extra = DoorCost;
                    break;
                case CFGOAPBlocker.Climb:
                    extra = ClimbCost;
                    break;
                case CFGOAPBlocker.Smash:
                    extra = HasComp<DoorComponent>(other) ? LockedDoorCost : GlassCost;
                    break;
            }

            if (HasGivenUp(field, other))
                return CFGOAPFlowFieldBuilder.Blocked;

            cost = Math.Max(cost, extra);
        }

        return cost;
    }

    /// <summary>The arrow under its feet, in world space.</summary>
    private bool TryGetArrow(EntityUid uid, CFGOAPFlowField field, out Vector2 arrow)
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
    private void LookForObstacle(Entity<CFGOAPSteeringComponent> ent, CFGOAPFlowField field)
    {
        if (!TryGetArrow(ent, field, out var arrow)
            || arrow == Vector2.Zero
            || !TryFeel(ent, _transform.GetWorldPosition(ent), arrow, ObstacleSight, out var hit)
            || Classify(ent, hit.Entity) is CFGOAPBlocker.None or CFGOAPBlocker.Solid
            || HasGivenUp(field, hit.Entity)
            || !IsOnRoute(ent, field, hit.Entity))
            return;

        ent.Comp.Obstacle = hit.Entity;
        ent.Comp.ObstacleField = field;
        ent.Comp.ObstacleStart = _timing.CurTime;
        WalkUpTo(ent, hit.Entity);
    }

    private void WalkUpTo(Entity<CFGOAPSteeringComponent> ent, EntityUid obstacle)
    {
        var profile = ProfileOf(ent);
        var arrive = new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.Arrive, Radius = profile.ArriveRadius };
        Steer(ent, WithHabits(profile, arrive), point: _transform.GetWorldPosition(obstacle), felt: ent.Comp.FeltMobs, through: obstacle);
    }

    /// <summary>Whether the arrows really lead into it. They may turn off just before something straight ahead.</summary>
    private bool IsOnRoute(EntityUid uid, CFGOAPFlowField field, EntityUid obstacle)
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

    /// <summary>
    /// Opens or breaks what is in the way. Returns false once that is over and it should move on. A creature running
    /// away has no <paramref name="goal"/>, so a clear line somewhere is no reason for it to stop.
    /// </summary>
    private bool WorkOnObstacle(Entity<CFGOAPSteeringComponent> ent, Vector2? goal)
    {
        var obstacle = ent.Comp.Obstacle!.Value;
        var kind = TerminatingOrDeleted(obstacle) ? CFGOAPBlocker.None : Classify(ent, obstacle);
        if (kind == CFGOAPBlocker.None || goal is { } point && CanWalkStraight(ent, point))
        {
            ClearObstacle(ent);
            return false;
        }

        // A personality only has so much patience for this. Mates sharing the field give up with it.
        var field = ent.Comp.ObstacleField ?? FieldOf(ent);
        var patience = TimeSpan.FromSeconds(ProfileOf(ent).SmashPatience);
        if (kind == CFGOAPBlocker.Solid || HasGivenUp(field, obstacle) || _timing.CurTime - ent.Comp.ObstacleStart >= patience)
        {
            GiveUp(ent, field, obstacle, goal ?? field.Goal);
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

        if (kind == CFGOAPBlocker.Door)
        {
            _doors.TryOpen(obstacle, user: ent);
            return true;
        }

        if (!ent.Comp.Working)
        {
            // Both stay put while they work, and moving would interrupt a climb.
            ent.Comp.Working = true;
            Hold(ent, obstacle);
            if (kind == CFGOAPBlocker.Smash)
            {
                _combatMode.SetInCombatMode(ent, true);
                EnsureComp<NPCMeleeCombatComponent>(ent).Target = obstacle;
            }
        }

        // Glass underfoot can trip it, so it keeps trying until it gets up and over.
        if (kind == CFGOAPBlocker.Climb
            && !_conditions.IsProne(ent)
            && TryComp(ent, out ClimbingComponent? climbing)
            && climbing.DoAfter == null)
            _climb.TryClimb(ent, ent, obstacle, out _, climbing: climbing);

        return true;
    }

    private void ClearObstacle(Entity<CFGOAPSteeringComponent> ent)
    {
        ent.Comp.Obstacle = null;
        ent.Comp.ObstacleField = null;
        ent.Comp.Working = false;
        ent.Comp.Rerouted = true;
        RemComp<NPCMeleeCombatComponent>(ent);
    }

    /// <summary>Takes it off the map and looks for another way. With none, it stops trying for a while.</summary>
    private void GiveUp(Entity<CFGOAPSteeringComponent> ent, CFGOAPFlowField field, EntityUid obstacle, Vector2 goal)
    {
        var until = _timing.CurTime + GiveUpMemory;
        field.GaveUp[obstacle] = until;
        ClearObstacle(ent);
        ent.Comp.Route.Clear();

        // Running away, any other way out will do, so there is nothing to stop trying.
        if (field.Away)
        {
            BuildFleeField(ent, field, goal);
            return;
        }

        BuildField(ent, field, goal);
        if (!TryGetArrow(ent, field, out _))
            ent.Comp.NoWayUntil = until;
    }
}
