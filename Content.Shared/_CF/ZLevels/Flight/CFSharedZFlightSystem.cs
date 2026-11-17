// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Content.Shared.CF.ZLevels.Flight.Components;
using Content.Shared.Actions;
using Content.Shared.Audio;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Stunnable;
using JetBrains.Annotations;
using Robust.Shared.Analyzers;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.ZLevels.Flight;

public abstract partial class CFSharedZFlightSystem : EntitySystem
{
    [Dependency] private CFSharedZLevelsSystem _zLevel = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;

    protected EntityQuery<CFZPhysicsComponent> ZPhyzQuery;

    public override void Initialize()
    {
        base.Initialize();

        ZPhyzQuery = GetEntityQuery<CFZPhysicsComponent>();
    }

    [SubscribeLocalEvent]
    private void OnFlightChasmAttempt(Entity<CFZFlyerComponent> ent, ref CFZLevelChasmAttempt args)
    {
        if (!ent.Comp.Active || args.Cancelled)
            return;

        args.Cancel();

        if (!ZPhyzQuery.TryComp(ent.Owner, out var zPhys))
            return;

        _zLevel.SetZPosition((ent.Owner, zPhys), 0f);

        if (zPhys.Velocity < 0)
            _zLevel.SetZVelocity((ent.Owner, zPhys), 0f);
    }

    [SubscribeLocalEvent]
    private void CheckWeightless(Entity<CFZFlyerComponent> ent, ref IsWeightlessEvent args)
    {
        if (!ent.Comp.Active || args.Handled)
            return;

        args.IsWeightless = true;
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnDamageDealt(Entity<CFZFlyerComponent> ent, ref DamageDealtEvent args)
    {
        if (!args.InterruptsDoAfters)
            return;

        var damageIncreased = false;
        foreach (var amount in args.Damage.DamageDict.Values)
        {
            if (amount <= 0)
                continue;

            damageIncreased = true;
            break;
        }

        if (!damageIncreased)
            return;

        DeactivateFlight((ent, ent));
    }

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<CFZFlyerComponent> ent, ref MobStateChangedEvent args)
    {
        DeactivateFlight((ent, ent));
    }

    [SubscribeLocalEvent]
    private void OnKnockDowned(Entity<CFZFlyerComponent> ent, ref KnockedDownEvent args)
    {
        DeactivateFlight((ent, ent));
    }

    [SubscribeLocalEvent]
    private void OnStunned(Entity<CFZFlyerComponent> ent, ref StunnedEvent args)
    {
        DeactivateFlight((ent, ent));
    }

    [SubscribeLocalEvent]
    private void OnStartFlight(Entity<CFZPhysicsComponent> ent, ref CFFlightStartedEvent args)
    {
        SetTargetHeight(ent.Owner, ent.Comp.CurrentZLevel);
        StartFlightVisuals(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnStopFlight(Entity<CFZPhysicsComponent> ent, ref CFFlightStoppedEvent args)
    {
        StopFlightVisuals(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnGetZVelocity(Entity<CFZFlyerComponent> ent, ref CFGetZVelocityEvent args)
    {
        if (!ent.Comp.Active)
            return;

        var zPhys = args.Target.Comp;
        var currentPos = zPhys.CurrentZLevel + zPhys.LocalPosition;
        var targetPos = ent.Comp.TargetMapHeight + 0.2f;
        var currentVelocity = zPhys.Velocity;

        var distanceToTarget = targetPos - currentPos;

        var targetVelocity = Math.Clamp(distanceToTarget * ent.Comp.FlightSpeed, -ent.Comp.FlightSpeed, ent.Comp.FlightSpeed);
        var velocityDelta = targetVelocity - currentVelocity;

        var upperBound = ent.Comp.TargetMapHeight + 0.9f;
        var lowerBound = ent.Comp.TargetMapHeight + 0.1f;

        var newVelocity = currentVelocity + velocityDelta;
        var nextPos = currentPos + newVelocity;

        if (nextPos > upperBound)
        {
            var maxAllowedVelocity = upperBound - currentPos;
            velocityDelta = maxAllowedVelocity - currentVelocity;
        }
        else if (nextPos < lowerBound)
        {
            var maxAllowedVelocity = lowerBound - currentPos;
            velocityDelta = maxAllowedVelocity - currentVelocity;
        }

        args.VelocityDelta = velocityDelta;
    }

    [SubscribeLocalEvent]
    private void OnRefreshVelocityRaise(Entity<CFZFlyerComponent> ent, ref CFRefreshVelocityRaiseEvent args)
    {
        if (ent.Comp.Active)
            args.Raise = true;
    }

    [SubscribeLocalEvent]
    private void OnGetGravity(Entity<CFZFlyerComponent> ent, ref CFCheckGravityEvent args)
    {
        if (ent.Comp.Active)
            args.Gravity *= 0;
    }

    [PublicAPI]
    public bool TryActivateFlight(Entity<CFZFlyerComponent?> ent, CFZPhysicsComponent? zPhys = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        if (!Resolve(ent, ref zPhys, false))
            return false;

        if (ent.Comp.Active)
            return false;

        var ev = new CFStartFlightAttemptEvent();
        RaiseLocalEvent(ent, ev);

        if (ev.Cancelled)
            return false;

        ent.Comp.Active = true;
        DirtyField(ent, ent.Comp, nameof(CFZFlyerComponent.Active));

        _zLevel.RefreshVelocityRaiseEvent((ent, zPhys));
        _zLevel.UpdateGravityState((ent, zPhys));
        _zLevel.WakeBody((ent, zPhys));
        _gravity.RefreshWeightless(ent.Owner);

        RaiseLocalEvent(ent, new CFFlightStartedEvent());
        return true;
    }

    [PublicAPI]
    public void DeactivateFlight(Entity<CFZFlyerComponent?> ent, CFZPhysicsComponent? zPhys = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (!Resolve(ent, ref zPhys, false))
            return;

        if (!ent.Comp.Active)
            return;

        ent.Comp.Active = false;
        DirtyField(ent, ent.Comp, nameof(CFZFlyerComponent.Active));

        _zLevel.RefreshVelocityRaiseEvent((ent, zPhys));
        _zLevel.UpdateGravityState((ent, zPhys));
        _gravity.RefreshWeightless(ent.Owner);

        RaiseLocalEvent(ent, new CFFlightStoppedEvent());
    }

    [PublicAPI]
    public void SetTargetHeight(Entity<CFZFlyerComponent?> ent, int targetHeight)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.TargetMapHeight = targetHeight;
        DirtyField(ent, ent.Comp, nameof(CFZFlyerComponent.TargetMapHeight));
    }

    private void StartFlightVisuals(Entity<CFZFlyerComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        _appearance.SetData(ent, CFFlightVisuals.Active, true);
        _ambient.SetAmbience(ent, true);
    }

    private void StopFlightVisuals(Entity<CFZFlyerComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        _appearance.SetData(ent, CFFlightVisuals.Active, false);
        _ambient.SetAmbience(ent, false);
    }
}

/// <summary>
/// Called on an entity when it attempts to start flight mode. Subscribe and cancel this event if you want to cancel your flight for any reason.
/// </summary>
public sealed partial class CFStartFlightAttemptEvent : CancellableEntityEventArgs;

/// <summary>
/// Called on an entity when it enters flight mode
/// </summary>
public sealed partial class CFFlightStartedEvent : EntityEventArgs;

/// <summary>
/// Called on an entity when it exits flight mode
/// </summary>
public sealed partial class CFFlightStoppedEvent : EntityEventArgs;


/// <summary>
/// Instant Action, raising the target flight level by 1
/// </summary>
public sealed partial class CFZFlightActionUp : InstantActionEvent
{
}

/// <summary>
/// Instant Action, lowering the target flight level by 1
/// </summary>
public sealed partial class CFZFlightActionDown : InstantActionEvent
{
}


[Serializable, NetSerializable]
public enum CFFlightVisuals
{
    Active,
}

/// <summary>
/// DoAfter event for starting flight with a delay
/// </summary>
[Serializable, NetSerializable]
public sealed partial class CFStartFlightDoAfterEvent : SimpleDoAfterEvent
{
}
