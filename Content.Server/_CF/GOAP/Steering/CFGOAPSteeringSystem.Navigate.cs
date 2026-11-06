using System.Numerics;
using Content.Server.CF.GOAP.Encounters;
using Content.Shared.CF.GOAP;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.GOAP.Steering;

/// <summary>Getting somewhere, and getting away, on flow fields.</summary>
public sealed partial class CFGOAPSteeringSystem
{
    private static readonly ProtoId<CFGOAPProfilePrototype> DefaultProfile = "Default";

    private static readonly TimeSpan NavigateInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Half the width of a creature's body, in tiles.</summary>
    private const float BodyHalfWidth = 0.35f;

    private static readonly TimeSpan FieldBuildInterval = TimeSpan.FromSeconds(1);

    /// <summary>How many tiles a field spreads from its goal, which caps what a build can cost.</summary>
    private const int FieldReach = 20;

    /// <summary>How many tiles a flee field spreads from the threat. It only has to outlast the next rebuild.</summary>
    private const int FleeReach = 16;

    /// <summary>A moving goal is only worth new arrows once it has gone this many tiles.</summary>
    private const float FieldGoalSlack = 1.5f;

    /// <summary>A follower going somewhere further from the field's goal than this does not use it.</summary>
    private const float FieldShareRange = 6f;

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CFGOAPEncounterSystem _encounters = default!;

    /// <summary>
    /// Heads straight there while a ray says the way is clear, which costs almost nothing. When something is in the
    /// way it follows the arrows instead. Only the navigator builds them. Arrows can lead through a door or window,
    /// which it then opens or breaks.
    /// </summary>
    public void Navigate(EntityUid uid, Vector2 goal, EntityUid? target)
    {
        if (!TryComp(uid, out CFGOAPSteeringComponent? steering))
            return;

        var ent = new Entity<CFGOAPSteeringComponent>(uid, steering);
        var now = _timing.CurTime;
        if (now < steering.NextNavigate)
            return;

        steering.NextNavigate = now + NavigateInterval;
        if (steering.Obstacle != null && WorkOnObstacle(ent, goal))
            return;

        var rerouted = steering.Rerouted;
        steering.Rerouted = false;
        if (CanWalkStraight(uid, goal))
        {
            if (!steering.UsingField && !rerouted)
                return;

            HeadStraightThere(uid, goal, target);
            return;
        }

        if (!TryGetField(ent, goal, out var field))
        {
            if (rerouted)
                HeadStraightThere(uid, goal, target);

            return;
        }

        if (!steering.UsingField || rerouted)
        {
            steering.UsingField = true;
            var flow = new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.FlowField };
            Steer(uid, WithHabits(ProfileOf(uid), flow), target, goal, CFGOAPFeltMobs.Chase, field);
        }

        LookForObstacle(ent, field);
    }

    /// <summary>
    /// Keeps running from <paramref name="threat"/>. Call it every tick. Returns false when there is no better place
    /// than where it stands.
    /// </summary>
    public bool Flee(EntityUid uid, Vector2 threat)
    {
        if (!TryComp(uid, out CFGOAPSteeringComponent? steering))
            return true;

        var ent = new Entity<CFGOAPSteeringComponent>(uid, steering);
        var field = FleeField(ent, out var navigator);
        var now = _timing.CurTime;
        if (now >= steering.NextNavigate)
        {
            steering.NextNavigate = now + NavigateInterval;
            if (steering.Obstacle == null || !WorkOnObstacle(ent, null))
                SteerAway(ent, field, navigator, threat);
        }

        return !field.Away || !TryGetArrow(uid, field, out var arrow) || arrow != Vector2.Zero;
    }

    /// <summary>
    /// A routed group runs on one field, built once. Anyone else runs on its own, because its pack may still be
    /// chasing on theirs.
    /// </summary>
    private CFGOAPFlowField FleeField(Entity<CFGOAPSteeringComponent> ent, out bool navigator)
    {
        if (_encounters.TryGetGroup(ent, out var group) && group.Routed)
        {
            navigator = group.Members.Count > 0 && group.Members[0] == ent.Owner;
            return group.Field;
        }

        navigator = true;
        return ent.Comp.OwnField;
    }

    private void SteerAway(Entity<CFGOAPSteeringComponent> ent, CFGOAPFlowField field, bool navigator, Vector2 threat)
    {
        var moved = (field.Goal - threat).Length();
        var stale = !field.Away || field.Arrows.Count == 0 || moved >= FieldGoalSlack && _timing.CurTime >= field.NextBuild;
        if (navigator && stale)
            BuildFleeField(ent, field, threat);

        var rerouted = ent.Comp.Rerouted;
        ent.Comp.Rerouted = false;

        // A follower can get here before its navigator has built anything, and off a grid nothing gets built.
        if (!field.Away || field.Arrows.Count == 0)
        {
            if (ent.Comp.UsingField || rerouted || ent.Comp.Behaviors.Count == 0)
            {
                ent.Comp.UsingField = false;
                var evade = new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.Evade };
                Steer(ent, WithHabits(ProfileOf(ent), evade), point: threat, felt: CFGOAPFeltMobs.Avoid);
            }

            ent.Comp.Point = threat;
            return;
        }

        if (!ent.Comp.UsingField || rerouted)
        {
            ent.Comp.UsingField = true;
            var flow = new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.FlowField };
            Steer(ent, WithHabits(ProfileOf(ent), flow), point: threat, felt: CFGOAPFeltMobs.Avoid, field: field);
        }

        ent.Comp.Point = threat;
        LookForObstacle(ent, field);
    }

    private void HeadStraightThere(EntityUid uid, Vector2 goal, EntityUid? target)
    {
        if (TryComp(uid, out CFGOAPSteeringComponent? steering))
            steering.UsingField = false;

        if (target is { } creature)
            Chase(uid, creature);
        else
            MoveTo(uid, goal);
    }

    /// <summary>
    /// Steers straight at a point and lets the feelers turn it aside, with no pathfinding at all.
    /// An enemy a ray touches on the way pulls it in.
    /// </summary>
    public void MoveTo(EntityUid uid, Vector2 point)
    {
        var profile = ProfileOf(uid);
        var arrive = new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.Arrive, Radius = profile.ArriveRadius };
        Steer(uid, WithHabits(profile, arrive), point: point, felt: CFGOAPFeltMobs.Chase);
    }

    /// <summary>The same for a creature, aiming for where it is heading.</summary>
    public void Chase(EntityUid uid, EntityUid target)
    {
        var pursuit = new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.Pursuit };
        Steer(uid, WithHabits(ProfileOf(uid), pursuit), target, felt: CFGOAPFeltMobs.Chase);
    }

    /// <summary>The behaviours it was asked for, plus the habits its personality adds to all its movement.</summary>
    public List<CFGOAPSteeringBehavior> WithHabits(CFGOAPProfilePrototype profile, params CFGOAPSteeringBehavior[] behaviors)
    {
        var list = new List<CFGOAPSteeringBehavior>(behaviors);
        if (profile.WallFollow > 0f)
            list.Add(new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.WallFollow, Weight = profile.WallFollow });

        if (profile.Separation > 0f)
            list.Add(new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.Separation, Weight = profile.Separation });

        return list;
    }

    private CFGOAPProfilePrototype ProfileOf(EntityUid uid)
    {
        return _prototypes.Index(CompOrNull<CFGOAPComponent>(uid)?.Profile ?? DefaultProfile);
    }

    /// <summary>A ray to the goal. Reaching a creature means nothing stood in the way.</summary>
    public bool IsLineClear(EntityUid uid, Vector2 goal)
    {
        var from = _transform.GetWorldPosition(uid);
        var line = goal - from;
        var length = line.Length();
        if (length <= 0f || !TryFeel(uid, from, line / length, length, out var hit))
            return true;

        return HasComp<MobStateComponent>(hit.Entity);
    }

    /// <summary>
    /// Whether its body fits along that line. A thin ray goes through a gap whose corner the body catches on, and a
    /// creature sent straight at it never gets through, so a ray runs down each side as well.
    /// </summary>
    public bool CanWalkStraight(EntityUid uid, Vector2 goal)
    {
        if (!IsLineClear(uid, goal))
            return false;

        var from = _transform.GetWorldPosition(uid);
        var line = goal - from;
        var length = line.Length();
        if (length <= 0f)
            return true;

        var direction = line / length;
        var side = new Vector2(direction.Y, -direction.X) * BodyHalfWidth;
        return IsSideClear(uid, from + side, direction, length) && IsSideClear(uid, from - side, direction, length);
    }

    private bool IsSideClear(EntityUid uid, Vector2 from, Vector2 direction, float length)
    {
        return !TryFeel(uid, from, direction, length, out var hit) || HasComp<MobStateComponent>(hit.Entity);
    }

    /// <summary>
    /// The field this creature should read. The first living member of a group is its navigator and keeps the field
    /// up to date. A creature alone is its own navigator. Followers only ever read, so a group costs one build.
    /// </summary>
    private bool TryGetField(Entity<CFGOAPSteeringComponent> ent, Vector2 goal, out CFGOAPFlowField field)
    {
        field = FieldOf(ent);
        var navigator = !_encounters.TryGetGroup(ent, out var group) || group.Members.Count > 0 && group.Members[0] == ent.Owner;

        var moved = (field.Goal - goal).Length();
        if (navigator && (field.Away || field.Arrows.Count == 0 || moved >= FieldGoalSlack && _timing.CurTime >= field.NextBuild))
            BuildField(ent, field, goal);

        return !field.Away && field.Arrows.Count > 0 && (field.Goal - goal).Length() < FieldShareRange;
    }

    private CFGOAPFlowField FieldOf(Entity<CFGOAPSteeringComponent> ent)
    {
        return _encounters.TryGetGroup(ent, out var group) ? group.Field : ent.Comp.OwnField;
    }

    /// <summary>Fills in the arrows. Walls and dangerous tiles are blocked, and doors and glass are slow going.</summary>
    public void BuildField(EntityUid uid, CFGOAPFlowField field, Vector2 goal)
    {
        field.Goal = goal;
        field.Away = false;
        field.NextBuild = _timing.CurTime + FieldBuildInterval;
        if (Transform(uid).GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return;

        field.Arrows = CFGOAPFlowFieldBuilder.Build(
            _maps.WorldToTile(grid, gridComp, goal),
            tile => TileCost(uid, field, grid, gridComp, tile),
            FieldReach);
    }

    /// <summary>
    /// Fills in arrows away from a threat. A door it can open is fine, but something running for its life does not
    /// stop to break anything, so glass and locks count as walls.
    /// </summary>
    public void BuildFleeField(EntityUid uid, CFGOAPFlowField field, Vector2 threat)
    {
        field.Goal = threat;
        field.Away = true;
        field.NextBuild = _timing.CurTime + FieldBuildInterval;
        if (Transform(uid).GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return;

        field.Arrows = CFGOAPFlowFieldBuilder.BuildFlee(
            _maps.WorldToTile(grid, gridComp, threat),
            tile =>
            {
                var cost = TileCost(uid, field, grid, gridComp, tile);
                return cost > DoorCost ? CFGOAPFlowFieldBuilder.Blocked : cost;
            },
            FleeReach);
    }

    /// <summary>Whether it could stand there, by the same rules its flow fields use.</summary>
    public bool IsWalkable(EntityUid uid, MapCoordinates point)
    {
        if (Transform(uid).GridUid is not { } grid
            || !TryComp(grid, out MapGridComponent? gridComp)
            || !TryComp(uid, out CFGOAPSteeringComponent? steering))
            return !IsDangerous(point);

        var tile = _maps.WorldToTile(grid, gridComp, point.Position);
        return TileCost(uid, FieldOf((uid, steering)), grid, gridComp, tile) == 0;
    }
}
