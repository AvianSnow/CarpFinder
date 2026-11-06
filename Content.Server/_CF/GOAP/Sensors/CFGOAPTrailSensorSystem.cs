using Content.Server.CF.GOAP.Influence;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>What it has to go on when it cannot see its target: where it was, its scent, and nodes to check.</summary>
public sealed partial class CFGOAPTrailSensorSystem : EntitySystem
{
    [Dependency] private CFGOAPSystem _goap = default!;
    [Dependency] private CFGOAPInfluenceMapSystem _influence = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var ent = args.Creature;
        if (ent.Comp.Lkp is { } lkp && lkp.MapId == args.Here.MapId)
        {
            args.Facts |= CFGOAPFact.KnowsLkp;

            if ((lkp.Position - args.Here.Position).Length() <= CFGOAPSystem.LkpArrival + args.Profile.LkpKeepOut)
                ent.Comp.LkpReached = true;

            if (ent.Comp.LkpReached)
                args.Facts |= CFGOAPFact.AtLkp;
        }

        if (_influence.TryFollow(ent, CFGOAPInfluenceLayer.Scent, ent.Comp.Target, out _))
            args.Facts |= CFGOAPFact.ScentNearby;

        if (ent.Comp.NodesChecked < args.Profile.NodesToCheck && _goap.TryPickNode(ent, out _))
            args.Facts |= CFGOAPFact.NodeNearby;
    }
}
