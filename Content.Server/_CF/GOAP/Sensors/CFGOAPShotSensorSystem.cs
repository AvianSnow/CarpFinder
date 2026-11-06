using Content.Shared.CF.GOAP;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>Whether it could shoot its target from here: something loaded in hand, and nothing in the way.</summary>
public sealed partial class CFGOAPShotSensorSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if ((args.Facts & CFGOAPFact.TargetVisible) == 0
            || args.Creature.Comp.Target is not { } target
            || !_gun.TryGetGun(uid, out var gun))
            return;

        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(gun, ref ammo);
        if (ammo.Count <= 0)
            return;

        // The check stock NPC shooting makes, so glass it can see through does not count as a shot.
        var distance = (_transform.GetMapCoordinates(target).Position - args.Here.Position).Length();
        if (_interaction.InRangeUnobstructed(uid, target, distance + 0.1f, CollisionGroup.Impassable | CollisionGroup.InteractImpassable))
            args.Facts |= CFGOAPFact.HasShot;
    }
}
