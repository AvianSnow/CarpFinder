using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>
/// Whether it is near where it started. It may roam as far as its home range, but once it sets off home it keeps going
/// until it is really there. A home it cannot get to, such as one on another level, counts as here.
/// </summary>
public sealed partial class CFGOAPHomeSensorSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFGOAPSystem _goap = default!;

    [SubscribeLocalEvent]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var creature = args.Creature;
        var range = creature.Comp.GoingHome ? CFGOAPSystem.HomeArrival : creature.Comp.HomeRange;
        if (!_goap.TryGetHome(creature, out var home)
            || (home.Position - _transform.GetMapCoordinates(creature).Position).Length() <= range)
            args.Facts |= CFGOAPFact.AtHome;
    }
}
