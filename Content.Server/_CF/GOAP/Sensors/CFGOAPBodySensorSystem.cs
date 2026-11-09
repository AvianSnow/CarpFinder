using Content.Server.CF.GOAP.Steering;
using Content.Shared.Atmos.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.GOAP;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>How the creature itself is doing.</summary>
public sealed partial class CFGOAPBodySensorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFGOAPSystem _goap = default!;
    [Dependency] private CFGOAPSteeringSystem _steering = default!;

    [SubscribeLocalEvent]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if (_actions.CanAffordActions(uid, 1))
            args.Facts |= CFGOAPFact.ActionsLeft;
        if (_goap.Health(uid) < args.Profile.HurtHealth)
            args.Facts |= CFGOAPFact.Hurt;
        if (_conditions.IsProne(uid))
            args.Facts |= CFGOAPFact.Prone;
        if (_conditions.Frightened(uid) > 0)
            args.Facts |= CFGOAPFact.Frightened;
        if (TryComp(uid, out CFStealthComponent? stealth) && stealth.Active)
            args.Facts |= CFGOAPFact.Hiding;
        if (_timing.CurTime < args.Creature.Comp.CorneredUntil)
            args.Facts |= CFGOAPFact.Cornered;

        // Rolling on lava only lights it again, so it gets off first.
        if (TryComp(uid, out FlammableComponent? fire) && fire.OnFire && !_steering.IsDangerous(args.Here))
            args.Facts |= CFGOAPFact.OnFire;

        if (TryComp(uid, out CFActionPoolComponent? pool))
        {
            if (pool.MapAttacks >= 1)
                args.Facts |= CFGOAPFact.Attacked;
            if (pool.MapAttacks >= 2)
                args.Facts |= CFGOAPFact.AttackedTwice;
        }
    }
}
