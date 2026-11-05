using System.Numerics;
using Content.Shared.CF.Detection;
using Content.Shared.CF.NPC;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.CF.NPC;

/// <summary>What changes for a creature that is part of an encounter.</summary>
public sealed partial class CFGoapSystem
{
    /// <summary>How far from the target a flanking slot is, in tiles.</summary>
    private const float FlankDistance = 1f;

    /// <summary>Near enough to the slot's side of the target to count, in degrees. Melee pulls a creature in.</summary>
    private const double FlankTolerance = 50;

    private const float FlankRange = 1.6f;

    private static readonly TimeSpan FlankUpdateInterval = TimeSpan.FromSeconds(0.5);

    private static readonly TimeSpan NavigateInterval = TimeSpan.FromSeconds(0.5);

    private static readonly TimeSpan FieldBuildInterval = TimeSpan.FromSeconds(1);

    /// <summary>How many tiles a field spreads from its goal, which caps what a build can cost.</summary>
    private const int FieldReach = 20;

    /// <summary>A moving goal is only worth new arrows once it has gone this many tiles.</summary>
    private const float FieldGoalSlack = 1.5f;

    /// <summary>A follower going somewhere further from the field's goal than this does not use it.</summary>
    private const float FieldShareRange = 6f;

    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private TurfSystem _turf = default!;

    private bool TryGetGroup(EntityUid uid, out CFEncounterSpawnerComponent group)
    {
        group = default!;
        return TryComp(uid, out CFEncounterMemberComponent? member) && TryComp(member.Group, out group!);
    }

    /// <summary>
    /// The group goes for one creature, so it can surround it. A member only joins in on a target it can see itself.
    /// </summary>
    private EntityUid PackTarget(EntityUid uid, EntityUid seen)
    {
        if (!TryGetGroup(uid, out var group))
            return seen;

        if (group.Target is { } shared && !TerminatingOrDeleted(shared) && _mobState.IsAlive(shared))
        {
            return _stealth.Detection(shared, uid) != CFDetection.Undetected && _examine.InRangeUnOccluded(uid, shared, SightTiles)
                ? shared
                : seen;
        }

        group.Target = seen;
        group.FlankAngle = _random.NextFloat(0f, MathF.Tau);
        return seen;
    }

    private CFGoapFact PackFacts(Entity<CFGoapComponent> ent, MapCoordinates here, bool targetVisible)
    {
        CFGoapFact facts = 0;
        if (TryComp(ent, out CFStealthComponent? stealth) && stealth.Active)
            facts |= CFGoapFact.Hiding;

        if (!TryGetGroup(ent, out var group))
            return facts;

        if (group.Routed)
            facts |= CFGoapFact.Routed;

        if (targetVisible && ent.Comp.Target is { } target && IsAtFlank(ent, group, target, here))
            facts |= CFGoapFact.AtFlank;

        return facts;
    }

    private bool IsAtFlank(EntityUid uid, CFEncounterSpawnerComponent group, EntityUid target, MapCoordinates here)
    {
        var from = _transform.GetMapCoordinates(target).Position;
        var offset = here.Position - from;
        if (offset.Length() > FlankRange || offset == Vector2.Zero)
            return false;

        var slot = new Angle(SlotAngle(uid, group));
        return Math.Abs(Angle.ShortestDistance(new Angle(MathF.Atan2(offset.Y, offset.X)), slot).Degrees) <= FlankTolerance;
    }

    private static float SlotAngle(EntityUid uid, CFEncounterSpawnerComponent group)
    {
        return CFFlankSlots.Angle(Math.Max(0, group.Members.IndexOf(uid)), group.FlankAngle);
    }

    /// <summary>Heads for this creature's side of the target, or straight at it if a slot is blocked.</summary>
    private void MoveToFlank(Entity<CFGoapComponent> ent)
    {
        if (ent.Comp.Target is not { } target
            || TerminatingOrDeleted(target)
            || !TryGetGroup(ent, out var group))
            return;

        var at = _transform.GetMapCoordinates(target);
        var angle = SlotAngle(ent, group);
        var slot = at.Offset(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * FlankDistance);
        if (IsWalkable(target, slot) && _interaction.InRangeUnobstructed(ent, slot, SightTiles))
            MoveTo(ent, slot.Position);
        else
            Chase(ent, target);
    }

    /// <summary>
    /// Heads straight there while a ray says the way is clear, which costs almost nothing. When something is in the
    /// way it follows the arrows instead. Only the navigator builds them.
    /// </summary>
    public void Navigate(Entity<CFGoapComponent> ent, Vector2 goal, EntityUid? target)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextNavigate)
            return;

        ent.Comp.NextNavigate = now + NavigateInterval;
        if (IsLineClear(ent, goal))
        {
            if (!ent.Comp.UsingField)
                return;

            ent.Comp.UsingField = false;
            if (target is { } creature)
                Chase(ent, creature);
            else
                MoveTo(ent, goal);

            return;
        }

        if (TryGetField(ent, goal, out var field) && !ent.Comp.UsingField)
        {
            ent.Comp.UsingField = true;
            var flow = new CFSteeringBehavior { Kind = CFSteeringKind.FlowField };
            _steering.Steer(ent, Steer(Profile(ent), flow), target, goal, CFFeltMobs.Chase, field);
        }
    }

    /// <summary>A ray to the goal. Reaching a creature means nothing stood in the way.</summary>
    private bool IsLineClear(EntityUid uid, Vector2 goal)
    {
        var from = _transform.GetWorldPosition(uid);
        var line = goal - from;
        var length = line.Length();
        if (length <= 0f || !_steering.TryFeel(uid, from, line / length, length, out var hit))
            return true;

        return HasComp<MobStateComponent>(hit.Entity);
    }

    /// <summary>
    /// The field this creature should read. The first living member of a group is its navigator and keeps the field
    /// up to date. A creature alone is its own navigator. Followers only ever read, so a group costs one build.
    /// </summary>
    private bool TryGetField(Entity<CFGoapComponent> ent, Vector2 goal, out CFFlowField field)
    {
        var navigator = true;
        field = ent.Comp.OwnField;
        if (TryGetGroup(ent, out var group))
        {
            field = group.Field;
            navigator = group.Members.Count > 0 && group.Members[0] == ent.Owner;
        }

        var moved = (field.Goal - goal).Length();
        if (navigator && (field.Arrows.Count == 0 || moved >= FieldGoalSlack && _timing.CurTime >= field.NextBuild))
            BuildField(ent, field, goal);

        return field.Arrows.Count > 0 && (field.Goal - goal).Length() < FieldShareRange;
    }

    /// <summary>Fills in the arrows. Walls and dangerous tiles are blocked, so the arrows lead around lava.</summary>
    public void BuildField(EntityUid uid, CFFlowField field, Vector2 goal)
    {
        field.Goal = goal;
        field.NextBuild = _timing.CurTime + FieldBuildInterval;
        if (Transform(uid).GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return;

        field.Arrows = CFFlowFieldBuilder.Build(
            _maps.WorldToTile(grid, gridComp, goal),
            tile => IsWalkable(uid, _maps.GridTileToWorld(grid, gridComp, tile)),
            FieldReach);
    }

    private bool IsWalkable(EntityUid near, MapCoordinates point)
    {
        if (_steering.IsDangerous(point))
            return false;

        if (Transform(near).GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return true;

        return !_turf.IsTileBlocked(grid, _maps.WorldToTile(grid, gridComp, point.Position), CollisionGroup.Impassable);
    }
}
