using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Server.CF.GOAP.Steering;

/// <summary>
/// Long trips to a place it already knows. A flow field only spreads so far, so a tile A* plans the whole way and the
/// creature walks it on one short field after another, which keeps doors, climbing and giving up working as usual.
/// This takes a place and never a creature, so it cannot be used to find anyone: where a target went comes from sight,
/// the influence maps and nodes.
/// </summary>
public sealed partial class CFGOAPSteeringSystem
{
    /// <summary>Further than this, in tiles, a field is not trusted to reach it.</summary>
    private const float RouteFrom = 14f;

    /// <summary>How many route tiles ahead each field is aimed. Well inside <see cref="FieldReach"/>.</summary>
    private const int RouteLeg = 12;

    /// <summary>With this few route tiles left of a leg it moves on, so it never has to slow down for a stop.</summary>
    private const int RouteLegLeft = 5;

    /// <summary>The most tiles one search looks at, which caps what it can cost.</summary>
    private const int RouteSearchLimit = 4096;

    private static readonly TimeSpan RouteInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Keeps it going to a place. Call it every tick. <paramref name="alone"/> is for a place only it cares about, such
    /// as its home, so it does not take over the field its group shares.
    /// </summary>
    public void Travel(EntityUid uid, Vector2 place, bool alone = false)
    {
        if (!TryComp(uid, out CFGOAPSteeringComponent? steering) || _timing.CurTime < steering.NextNavigate)
            return;

        var stop = NextStop((uid, steering), place, alone, out var onRoute);

        // Each creature on a route is at its own point along it, so a shared field could not suit them all.
        Navigate(uid, stop, null, alone || onRoute);
    }

    /// <summary>Where to aim now: the place itself while a field can manage, or the next stop on a route.</summary>
    private Vector2 NextStop(Entity<CFGOAPSteeringComponent> ent, Vector2 place, bool alone, out bool onRoute)
    {
        onRoute = false;
        if (CanWalkStraight(ent, place)
            || Transform(ent).GridUid is not { } grid
            || !TryComp(grid, out MapGridComponent? gridComp))
        {
            ent.Comp.Route.Clear();
            return place;
        }

        if (ent.Comp.RouteGrid != grid || (ent.Comp.RouteGoal - place).Length() >= FieldGoalSlack)
            ent.Comp.Route.Clear();

        if (ent.Comp.Route.Count == 0 && FieldWillDo(ent, place, alone))
            return place;

        if (ent.Comp.Route.Count == 0 && _timing.CurTime >= ent.Comp.NextRoute)
            PlanRoute(ent, place, grid, gridComp);

        if (ent.Comp.Route.Count == 0)
            return place;

        onRoute = true;
        return AdvanceRoute(ent, place, grid, gridComp);
    }

    /// <summary>
    /// Close enough, and the field reaches it. A field that has not been built for this place yet gets its chance:
    /// it may stand a few tiles from the goal but a long walk round. A field built with no arrows reaches nothing, which
    /// is how a sealed place shows up, so it does not get another chance.
    /// </summary>
    private bool FieldWillDo(Entity<CFGOAPSteeringComponent> ent, Vector2 place, bool alone)
    {
        if ((place - _transform.GetWorldPosition(ent)).Length() > RouteFrom)
            return false;

        var field = alone ? ent.Comp.OwnField : FieldOf(ent);
        var built = !field.Away && field.NextBuild > TimeSpan.Zero && (field.Goal - place).Length() < FieldGoalSlack;
        return !built || TryGetArrow(ent, field, out _);
    }

    private void PlanRoute(Entity<CFGOAPSteeringComponent> ent, Vector2 place, EntityUid grid, MapGridComponent gridComp)
    {
        var now = _timing.CurTime;
        ent.Comp.NextRoute = now + RouteInterval;

        var start = _maps.WorldToTile(grid, gridComp, _transform.GetWorldPosition(ent));
        var goal = _maps.WorldToTile(grid, gridComp, place);
        var field = ent.Comp.OwnField;
        if (!CFGOAPFlowFieldBuilder.TryFindRoute(
                start,
                goal,
                tile => TileCost(ent, field, grid, gridComp, tile),
                RouteSearchLimit,
                out var route))
        {
            ent.Comp.NoWayUntil = now + GiveUpMemory;
            return;
        }

        ent.Comp.Route = route;
        ent.Comp.RouteGrid = grid;
        ent.Comp.RouteGoal = place;
        ent.Comp.RouteAt = 0;
        ent.Comp.RouteStop = Math.Min(RouteLeg, route.Count - 1);
    }

    private Vector2 AdvanceRoute(Entity<CFGOAPSteeringComponent> ent, Vector2 place, EntityUid grid, MapGridComponent gridComp)
    {
        var route = ent.Comp.Route;
        var here = _transform.GetWorldPosition(ent);

        // One tile at a time, so it cannot jump to the part of the route on the far side of a wall.
        while (ent.Comp.RouteAt < ent.Comp.RouteStop
               && Vector2.Distance(here, _maps.GridTileToWorldPos(grid, gridComp, route[ent.Comp.RouteAt + 1]))
               <= Vector2.Distance(here, _maps.GridTileToWorldPos(grid, gridComp, route[ent.Comp.RouteAt])))
        {
            ent.Comp.RouteAt++;
        }

        var last = route.Count - 1;
        if (ent.Comp.RouteStop - ent.Comp.RouteAt <= RouteLegLeft)
            ent.Comp.RouteStop = Math.Min(ent.Comp.RouteAt + RouteLeg, last);

        return ent.Comp.RouteStop == last ? place : _maps.GridTileToWorldPos(grid, gridComp, route[ent.Comp.RouteStop]);
    }
}
