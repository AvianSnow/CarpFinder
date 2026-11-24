using Content.Shared.CF.Actions;
using Content.Shared.CF.GOAP;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>Whether its breath weapon would catch the target now. Any creature with one, so any brain can breathe.</summary>
public sealed partial class CFGOAPBreathSensorSystem : EntitySystem
{
    [Dependency] private CFBreathWeaponSystem _breath = default!;
    [Dependency] private Content.Shared.CF.Combat.CFMonsterShotSystem _shots = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if (!args.Has(CFGOAPFact.TargetVisible) || args.Creature.Comp.Target is not { } target)
            return;

        if (_breath.TryGetBreath(uid, out var breath)
            && _breath.IsReady(uid, breath.Value)
            && _breath.WouldCatch(uid, breath.Value, target))
        {
            args.Facts |= CFGOAPFact.CanBreathe;
            return;
        }

        // A ranged attack of its own, such as a spider's web, is used through the same step as a breath.
        if (_shots.TryGetReady(uid, out var shot) && _shots.Worthwhile(uid, shot, target))
            args.Facts |= CFGOAPFact.CanBreathe;
    }
}
