using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.Combat;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Items;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>What it is holding, and what useful items lie in sight. Steps use the same lookups to act on them.</summary>
public sealed partial class CFGOAPItemSensorSystem : EntitySystem
{
    private const float ItemTiles = 8f;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFGOAPSteeringSystem _steering = default!;
    [Dependency] private CFShieldSystem _shields = default!;

    [SubscribeLocalEvent]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if (HoldsWeapon(uid))
            args.Facts |= CFGOAPFact.HasWeapon;
        if (TryFindHeld(uid, CFItemUse.Heals, out _))
            args.Facts |= CFGOAPFact.HasHealingItem;
        if (_shields.TryGetUsableShield(uid, out var shield) && !shield.Comp.Raised)
            args.Facts |= CFGOAPFact.ShieldReady;
        if (TryFindItem(uid, args.Here, CFItemUse.Weapon, out _))
            args.Facts |= CFGOAPFact.WeaponNearby;
        if (TryFindItem(uid, args.Here, CFItemUse.Heals, out _))
            args.Facts |= CFGOAPFact.HealingNearby;
    }

    private bool HoldsWeapon(EntityUid uid)
    {
        if (TryComp(uid, out CFCombatOptionsComponent? style) && style.NaturalDie > 0)
            return true;

        // A monster's own attack, such as a stat block's bite.
        if (HasComp<CFMeleeWeaponComponent>(uid))
            return true;

        return TryFindHeld(uid, CFItemUse.Weapon, out _);
    }

    public bool TryFindHeld(EntityUid uid, CFItemUse use, out EntityUid item)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (TryComp(held, out CFItemUseComponent? data) && (data.Uses & use) != 0)
            {
                item = held;
                return true;
            }
        }

        item = default;
        return false;
    }

    public bool TryFindItem(EntityUid uid, MapCoordinates here, CFItemUse use, out EntityUid item)
    {
        item = default;
        var nearest = float.MaxValue;
        foreach (var candidate in _lookup.GetEntitiesInRange<CFItemUseComponent>(here, ItemTiles))
        {
            if ((candidate.Comp.Uses & use) == 0 || _containers.IsEntityInContainer(candidate))
                continue;

            var at = _transform.GetMapCoordinates(candidate);
            if (_steering.IsDangerous(at))
                continue;

            var distance = (at.Position - here.Position).Length();
            if (distance >= nearest || !_examine.InRangeUnOccluded(uid, candidate.Owner, ItemTiles))
                continue;

            nearest = distance;
            item = candidate;
        }

        return nearest < float.MaxValue;
    }
}
