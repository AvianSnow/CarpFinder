using System.Numerics;
using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.Companions;
using Content.Shared.CF.GOAP;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Steps;

/// <summary>
/// A companion keeps a place a little behind its master. While they walk it walks with them, matching their pace; when
/// they stop it walks up, slows down, and stands behind them until they move off again. Several companions of one
/// master fan out to either side. Round a corner it finds the way like any other walk.
/// </summary>
public sealed partial class CFGOAPFollowStepSystem : CFGOAPStepSystem
{
    /// <summary>How far behind its master it keeps, in tiles.</summary>
    private const float Behind = 1.5f;

    /// <summary>How far each further companion is turned out to the side, in radians.</summary>
    private const float SpreadAngle = MathF.PI / 4.5f;

    /// <summary>A master moving slower than this, in tiles a second, counts as standing still.</summary>
    private const float Walking = 0.3f;

    /// <summary>Once standing behind them, how far its place has to move before it sets off again, in tiles.</summary>
    private const float Resume = 1f;

    /// <summary>It starts slowing this far from its place, in tiles.</summary>
    private const float SlowRadius = 2f;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.Follow;

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        ent.Comp.NextFollowCheck = TimeSpan.Zero;
        ent.Comp.FollowStraight = false;
    }

    public override bool Run(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (!TryComp(ent, out CFCompanionComponent? companion)
            || !companion.Summoner.IsValid()
            || TerminatingOrDeleted(companion.Summoner)
            || !TryComp(ent, out CFGOAPSteeringComponent? steering))
            return true;

        var master = companion.Summoner;
        var here = _transform.GetMapCoordinates(ent);
        var masterAt = _transform.GetMapCoordinates(master);
        if (masterAt.MapId != here.MapId)
            return true;

        var velocity = CompOrNull<PhysicsComponent>(master)?.LinearVelocity ?? Vector2.Zero;
        if (velocity.Length() > Walking)
            ent.Comp.FollowHeading = Vector2.Normalize(velocity);

        var place = Place(ent, master, masterAt, here.Position);
        var now = _timing.CurTime;
        if (now >= ent.Comp.NextFollowCheck)
        {
            ent.Comp.NextFollowCheck = now + CheckInterval;
            var straight = Steering.CanWalkStraight(ent, place);
            if (straight && !IsFollowing(steering))
                Steering.Steer(ent, Steering.WithHabits(Goap.Profile(ent), Arrive()), point: place);

            ent.Comp.FollowStraight = straight;
        }

        if (!ent.Comp.FollowStraight)
        {
            // Round a corner, so the way there is not a straight line.
            Steering.Travel(ent, place, alone: true);
            return false;
        }

        // Moved along every tick, so it keeps pace without starting a new walk each time.
        steering.Point = place;
        steering.PointVelocity = velocity;
        return false;
    }

    /// <summary>
    /// Behind its master, by the way they last walked, turned out to one side or the other for each companion after the
    /// first. Before they have walked anywhere, behind is the side it already stands on.
    /// </summary>
    private Vector2 Place(Entity<CFGOAPComponent> ent, EntityUid master, MapCoordinates masterAt, Vector2 here)
    {
        var heading = ent.Comp.FollowHeading;
        if (heading == Vector2.Zero)
        {
            var away = masterAt.Position - here;
            heading = away.LengthSquared() > 0.0001f ? Vector2.Normalize(away) : Vector2.UnitY;
        }

        var turn = Turn(ent, master);
        var place = masterAt.Position - new Angle(turn).RotateVec(heading) * Behind;
        if (Steering.IsWalkable(ent, new MapCoordinates(place, masterAt.MapId)))
            return place;

        // A wall behind them. It keeps to its own side instead.
        var mine = here - masterAt.Position;
        return mine.LengthSquared() > 0.0001f
            ? masterAt.Position + Vector2.Normalize(mine) * Behind
            : masterAt.Position;
    }

    /// <summary>0 for the first companion, then out to alternate sides.</summary>
    private float Turn(Entity<CFGOAPComponent> ent, EntityUid master)
    {
        var index = 0;
        var query = EntityQueryEnumerator<CFCompanionComponent>();
        while (query.MoveNext(out var other, out var comp))
        {
            if (comp.Summoner == master && other.Id < ent.Owner.Id)
                index++;
        }

        if (index == 0)
            return 0f;

        var side = index % 2 == 1 ? 1f : -1f;
        return side * SpreadAngle * ((index + 1) / 2);
    }

    private static bool IsFollowing(CFGOAPSteeringComponent steering)
    {
        return steering.Target == null
               && steering.Point != null
               && !steering.UsingField
               && steering.Behaviors.Count > 0
               && steering.Behaviors[0] is { Kind: CFGOAPSteeringKind.Arrive, Radius: SlowRadius, Resume: Resume };
    }

    private static CFGOAPSteeringBehavior Arrive()
    {
        return new CFGOAPSteeringBehavior { Kind = CFGOAPSteeringKind.Arrive, Radius = SlowRadius, Resume = Resume };
    }
}
