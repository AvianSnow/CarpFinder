using System.Numerics;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Tumble Through: running into an enemy and keeping on pushing. Walking never tumbles.
/// Stock mobs do not collide. They push each other through <see cref="MobCollisionComponent"/>,
/// so passing through turns that pushing off.
/// </summary>
public sealed partial class CFManeuverSystem
{
    /// <summary>Centers closer than this are touching. Mobs have a 0.35 radius.</summary>
    private const float TouchRange = 0.85f;

    /// <summary>How squarely the creature has to be heading at the enemy.</summary>
    private const float MinPushDot = 0.7f;

    /// <summary>Tumble Behind lasts until the tumbler's next attack, at most its next turn.</summary>
    private const float TumbleBehindSeconds = 6f;

    private static readonly TimeSpan PushTime = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan TumbleInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ThroughTime = TimeSpan.FromSeconds(1.5);

    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>NPCs have no move input, so they call <see cref="TryTumble"/> directly.</summary>
    private void UpdateTumble()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<InputMoverComponent, CFActionPoolComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out var mover, out _, out _))
        {
            if (!mover.HasDirectionalMovement
                || !mover.Sprinting
                || !TryPushedEnemy(uid, mover.WishDir, out var target))
            {
                if (TryComp(uid, out CFTumbleComponent? idle))
                    idle.PushTarget = null;

                continue;
            }

            var tumble = EnsureComp<CFTumbleComponent>(uid);
            if (tumble.PushTarget != target)
            {
                tumble.PushTarget = target;
                tumble.PushSince = now;
                continue;
            }

            if (now - tumble.PushSince < PushTime)
                continue;

            tumble.PushTarget = null;
            TryTumble(uid, target);
        }
    }

    /// <summary>The nearest living enemy touching this creature, in the direction it is heading.</summary>
    public bool TryPushedEnemy(EntityUid user, Vector2 direction, out EntityUid target)
    {
        target = default;
        if (direction == Vector2.Zero)
            return false;

        var heading = Vector2.Normalize(direction);
        var here = _transform.GetMapCoordinates(user);
        var nearest = float.MaxValue;
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(here, TouchRange))
        {
            var uid = mob.Owner;
            if (uid == user || !_mobState.IsAlive(uid) || !_factions.CanHarm(user, uid))
                continue;

            var toTarget = _transform.GetMapCoordinates(uid).Position - here.Position;
            var distance = toTarget.Length();
            if (distance <= 0f || distance >= nearest || Vector2.Dot(toTarget / distance, heading) < MinPushDot)
                continue;

            nearest = distance;
            target = uid;
        }

        return nearest < float.MaxValue;
    }

    /// <summary>
    /// One action, Acrobatics against the enemy's Reflex DC. A success slides through, and a failure stops dead.
    /// The spin plays either way, so everyone sees the action was spent.
    /// </summary>
    public bool TryTumble(EntityUid user, EntityUid target)
    {
        var now = _timing.CurTime;
        var tumble = EnsureComp<CFTumbleComponent>(user);
        if (!_net.IsServer || !HasStats(user) || now < tumble.NextTumble || !_actions.TrySpendActions(user, 1))
            return false;

        tumble.NextTumble = now + TumbleInterval;
        RaiseNetworkEvent(new CFTumbleSpinEvent(GetNetEntity(user)), Filter.Pvs(user));

        var degree = _conditions.RollSkill(user, target, "Acrobatics", CFAbility.Dexterity, CFDefenseType.Reflex);
        if (degree is not (CFDegree.Success or CFDegree.CriticalSuccess))
        {
            _physics.SetLinearVelocity(user, Vector2.Zero);
            _popup.PopupEntity(Loc.GetString("cf-act-tumble-fail"), user, user);
            return false;
        }

        tumble.ThroughUntil = now + ThroughTime;
        Dirty(user, tumble);

        if (TryComp(user, out CFCombatOptionsComponent? style) && style.TumbleBehind)
        {
            _conditions.SetOffGuardTo(target, user, TumbleBehindSeconds);
            _popup.PopupEntity(Loc.GetString("cf-act-tumble-behind"), user, user);
            return true;
        }

        _popup.PopupEntity(Loc.GetString("cf-act-tumble"), user, user);
        return true;
    }

    public bool IsTumblingThrough(EntityUid uid)
    {
        return TryComp(uid, out CFTumbleComponent? tumble) && _timing.CurTime < tumble.ThroughUntil;
    }

    [SubscribeLocalEvent]
    private void OnAttemptMobCollide(Entity<CFTumbleComponent> ent, ref AttemptMobCollideEvent args)
    {
        args.Cancelled |= _timing.CurTime < ent.Comp.ThroughUntil;
    }

    [SubscribeLocalEvent]
    private void OnAttemptMobTargetCollide(Entity<CFTumbleComponent> ent, ref AttemptMobTargetCollideEvent args)
    {
        args.Cancelled |= _timing.CurTime < ent.Comp.ThroughUntil;
    }
}
