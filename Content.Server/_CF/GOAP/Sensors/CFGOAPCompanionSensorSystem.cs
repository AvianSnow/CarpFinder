using Content.Shared.CF.Companions;
using Content.Shared.Mobs.Systems;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>
/// A companion's home is wherever its master is, so the home goal and the lair leash keep it close and bring it back.
/// It runs before the home sensor, which reads home.
/// </summary>
public sealed partial class CFGOAPCompanionSensorSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    [SubscribeLocalEvent(before: [typeof(CFGOAPHomeSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        if (!TryComp(args.Creature, out CFCompanionComponent? companion))
            return;

        var master = companion.Summoner;
        if (!master.IsValid() || TerminatingOrDeleted(master) || _mobState.IsDead(master))
            return;

        if (Transform(master).MapID != Transform(args.Creature).MapID)
            return;

        args.Creature.Comp.Home = _transform.GetMoverCoordinates(master);
    }
}
