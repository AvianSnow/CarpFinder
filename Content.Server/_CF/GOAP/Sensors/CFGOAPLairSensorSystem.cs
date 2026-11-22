using Content.Shared.CF.Companions;
using Content.Shared.CF.GOAP;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>
/// Facts for a creature that keeps a lair. <see cref="CFGOAPFact.Guarding"/> keeps it from closing on a far target, and
/// <see cref="CFGOAPFact.LeftLair"/> sends it home. Home is <see cref="CFGOAPComponent"/>'s, and "at home" is what the home
/// sensor says, so this runs after it.
/// </summary>
public sealed partial class CFGOAPLairSensorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFGOAPSystem _goap = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem), typeof(CFGOAPHomeSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var creature = args.Creature;
        if (!TryComp(creature, out CFGOAPLairComponent? lair))
            return;

        if (args.Has(CFGOAPFact.TargetVisible) && creature.Comp.Target is { } target)
        {
            var distance = (_transform.GetMapCoordinates(target).Position - args.Here.Position).Length();
            if (distance > lair.EngageTiles)
                args.Facts |= CFGOAPFact.Guarding;
        }

        var now = _timing.CurTime;

        // A companion its master sent at a creature keeps after it while that creature lives. Pointing is an order.
        if (TryComp(creature, out CFCompanionComponent? companion) && companion.Target.IsValid() && !companion.Recalled)
        {
            lair.AwaySince = null;
            lair.Returning = false;
            return;
        }

        if (args.Has(CFGOAPFact.AtHome))
        {
            lair.AwaySince = null;
            lair.Returning = false;
            return;
        }

        lair.AwaySince ??= now;
        var tooFar = _goap.TryGetHome(creature, out var home)
                     && (home.Position - args.Here.Position).Length() > lair.LeashTiles;
        if (tooFar || now - lair.AwaySince.Value >= lair.ChaseTime)
            lair.Returning = true;

        if (lair.Returning)
            args.Facts |= CFGOAPFact.LeftLair;
    }
}
