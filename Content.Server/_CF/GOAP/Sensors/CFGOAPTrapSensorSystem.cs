using Content.Shared.CF.GOAP;
using Content.Shared.CF.Hazards;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>Whether a trap layer has a trap to lay and the time to lay it.</summary>
public sealed partial class CFGOAPTrapSensorSystem : EntitySystem
{
    /// <summary>Nearer than this, building is no use: the enemy is already past it.</summary>
    private const float MinimumTiles = 3f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFTrapLayerSystem _traps = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem), typeof(CFGOAPStandoffSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if (!TryComp(uid, out CFTrapLayerComponent? layer)
            || layer.Charges <= 0
            || _timing.CurTime < layer.NextLay
            || !args.Has(CFGOAPFact.TargetVisible)
            || args.Has(CFGOAPFact.TooClose)
            || args.Creature.Comp.Target is not { } target)
            return;

        var toTarget = _transform.GetMapCoordinates(target).Position - args.Here.Position;
        if (toTarget.Length() >= MinimumTiles && _traps.TryFindSpot(uid, toTarget, layer.SpacingTiles, out _))
            args.Facts |= CFGOAPFact.CanLayTrap;
    }
}
