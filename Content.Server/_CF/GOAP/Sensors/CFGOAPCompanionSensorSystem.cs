using Content.Shared.CF.Companions;
using Content.Shared.CF.GOAP;
using Content.Shared.Mobs.Systems;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>
/// A companion's home is wherever its master is, so the home goal and the lair leash keep it close and bring it back.
/// It runs before the home sensor, which reads home.
/// </summary>
public sealed partial class CFGOAPCompanionSensorSystem : EntitySystem
{
    /// <summary>How close to its master a recalled companion has to get before it fights again, in tiles.</summary>
    private const float RecallArrival = 2.5f;

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
        args.Facts |= CFGOAPFact.HasMaster;

        // Back beside its master, a recall is done and it fights for itself again.
        if (companion.Recalled
            && (_transform.GetMapCoordinates(master).Position - args.Here.Position).Length() <= RecallArrival)
            companion.Recalled = false;
    }
}
