using Content.Shared.CF.GOAP;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>Whether a creature with a ranged weapon out is still happy with it. Only creatures with a standoff pay for this.</summary>
public sealed partial class CFGOAPStandoffSensorSystem : EntitySystem
{
    private static readonly TimeSpan CaughtHold = TimeSpan.FromSeconds(2);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if (!TryComp(uid, out CFGOAPStandoffComponent? standoff))
            return;

        // With a melee weapon out it fights like anything else, and nothing here applies.
        if (!_gun.TryGetGun(uid, out var gun))
        {
            standoff.InReachSince = null;
            return;
        }

        var goap = args.Creature.Comp;
        var now = _timing.CurTime;
        if (!args.Has(CFGOAPFact.TargetInReach))
            standoff.InReachSince = null;
        else if ((standoff.InReachSince ??= now) + standoff.CaughtTime <= now)
            goap.CorneredUntil = now + CaughtHold;

        var cornered = now < goap.CorneredUntil;
        if (cornered)
            args.Facts |= CFGOAPFact.Cornered;

        if (args.Has(CFGOAPFact.TargetVisible) && goap.Target is { } target)
        {
            var limit = CFGOAPSystem.Knows(goap, CFGOAPFact.TooClose) ? standoff.ClearTiles : standoff.KeepTiles;
            if ((_transform.GetMapCoordinates(target).Position - args.Here.Position).Length() <= limit)
                args.Facts |= CFGOAPFact.TooClose;
        }

        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(gun, ref ammo);
        if ((cornered || ammo.Count <= 0) && _inventory.TryGetSlotEntity(uid, standoff.SidearmSlot, out _))
            args.Facts |= CFGOAPFact.ShouldDraw;
    }
}
