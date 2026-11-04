using System.Numerics;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Homing bolts for <see cref="CFDelivery.Projectile"/>. The source resolves when the bolt reaches its creature.
/// </summary>
public sealed partial class CFEffectSystem
{
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>Fires bolts that fan out slightly and each home on <paramref name="target"/>.</summary>
    public void LaunchBolts(EntityUid source, EntityUid user, EntityUid target, int count)
    {
        if (!TryComp(source, out CFProjectileDeliveryComponent? delivery))
            return;

        count = Math.Max(1, count);
        var from = _transform.GetMapCoordinates(user);
        var to = _transform.GetMapCoordinates(target);
        var direction = to.Position - from.Position;
        if (direction.LengthSquared() < 0.04f)
            direction = new Vector2(1f, 0f);

        direction = Vector2.Normalize(direction);
        var side = new Vector2(-direction.Y, direction.X);
        for (var i = 0; i < count; i++)
        {
            var offset = (i - (count - 1) / 2f) * 0.35f;
            var origin = from.Position + direction * (0.75f + i * 0.12f) + side * offset;
            var bolt = Spawn(delivery.BoltPrototype, new MapCoordinates(origin, from.MapId));
            _gun.ShootProjectile(bolt, direction, Vector2.Zero, null, user, delivery.Speed);

            var homing = EnsureComp<CFHomingProjectileComponent>(bolt);
            homing.Target = target;
            homing.User = user;
            homing.Source = source;
            homing.Speed = delivery.Speed;
        }
    }

    private void SteerBolts()
    {
        var query = EntityQueryEnumerator<CFHomingProjectileComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var homing, out var physics))
        {
            if (homing.Resolved || !Exists(homing.Target))
            {
                QueueDel(uid);
                continue;
            }

            var targetPos = _transform.GetWorldPosition(homing.Target);
            var pos = _transform.GetWorldPosition(uid);
            var delta = targetPos - pos;
            var distance = delta.Length();
            if (distance < 0.35f)
            {
                ResolveBolt(homing);
                QueueDel(uid);
                continue;
            }

            var direction = delta / distance;
            _physics.SetLinearVelocity(uid, direction * homing.Speed, body: physics);
            _transform.SetWorldRotation(uid, direction.ToWorldAngle());
        }
    }

    [SubscribeLocalEvent]
    private void OnBoltHit(Entity<CFHomingProjectileComponent> bolt, ref ProjectileHitEvent args)
    {
        if (!_net.IsServer || bolt.Comp.Resolved || args.Target != bolt.Comp.Target)
            return;

        ResolveBolt(bolt.Comp);
    }

    private void ResolveBolt(CFHomingProjectileComponent homing)
    {
        if (homing.Resolved)
            return;

        homing.Resolved = true;
        if (!Exists(homing.Target) || !Exists(homing.Source))
            return;

        if (CanTarget(homing.Source, homing.User, homing.Target))
            ResolveTarget(homing.Source, homing.User, homing.Target);
    }

    /// <summary>A bolt only collides with its own creature and walls. It passes through everything else.</summary>
    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<CFHomingProjectileComponent> bolt, ref PreventCollideEvent args)
    {
        if (!IsProjectileFixture(bolt.Owner, args.OurFixture))
        {
            args.Cancelled = true;
            return;
        }

        if (args.OtherEntity == bolt.Comp.Target)
            return;

        if (args.OtherEntity == bolt.Comp.User
            || HasComp<MobStateComponent>(args.OtherEntity)
            || HasComp<CFHomingProjectileComponent>(args.OtherEntity))
            args.Cancelled = true;
    }

    private bool IsProjectileFixture(EntityUid uid, Fixture fixture)
    {
        if (!TryComp(uid, out FixturesComponent? manager))
            return true;

        foreach (var (id, candidate) in manager.Fixtures)
        {
            if (!ReferenceEquals(candidate, fixture))
                continue;

            return id == SharedProjectileSystem.ProjectileFixture;
        }

        return true;
    }
}
