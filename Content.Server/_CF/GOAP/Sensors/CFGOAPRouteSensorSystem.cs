using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>Whether it has run out of ways to get where it was going.</summary>
public sealed partial class CFGOAPRouteSensorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFGOAPSteeringSystem _steering = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if (!TryComp(uid, out CFGOAPSteeringComponent? steering) || _timing.CurTime >= steering.NoWayUntil)
            return;

        // The target came out where it can be reached after all.
        if (args.Has(CFGOAPFact.TargetVisible)
            && args.Creature.Comp.Target is { } target
            && _steering.CanWalkStraight(uid, _transform.GetWorldPosition(target)))
            steering.NoWayUntil = TimeSpan.Zero;
        else
            args.Facts |= CFGOAPFact.NoWay;
    }
}
