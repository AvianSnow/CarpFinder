using Content.Shared.CF.Actions;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>Whether its breath weapon would catch the target now. Any creature with one, so any brain can breathe.</summary>
public sealed partial class CFGOAPBreathSensorSystem : EntitySystem
{
    [Dependency] private CFBreathWeaponSystem _breath = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if (!args.Has(CFGOAPFact.TargetVisible)
            || args.Creature.Comp.Target is not { } target
            || !_breath.TryGetBreath(uid, out var breath)
            || !_breath.IsReady(uid, breath.Value))
            return;

        if (_breath.WouldCatch(uid, breath.Value, target))
            args.Facts |= CFGOAPFact.CanBreathe;
    }
}
