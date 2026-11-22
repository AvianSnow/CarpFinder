using Content.Server.CF.GOAP.Encounters;
using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.Factions;
using Content.Shared.CF.GOAP;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>Who is on its side, and how its group is doing.</summary>
public sealed partial class CFGOAPGroupSensorSystem : EntitySystem
{
    private const float AllyTiles = 6f;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFGOAPEncounterSystem _encounters = default!;
    [Dependency] private CFGOAPSteeringSystem _steering = default!;

    [SubscribeLocalEvent(after: [typeof(CFGOAPSightSensorSystem)])]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if (HasAlliesNearby(uid, args.Here))
            args.Facts |= CFGOAPFact.AlliesNearby;

        if (!_encounters.TryGetGroup(uid, out var group))
            return;

        if (group.Routed)
            args.Facts |= CFGOAPFact.Routed;

        if (!args.Has(CFGOAPFact.TargetVisible) || args.Creature.Comp.Target is not { } target)
            return;

        if (_encounters.IsAtFlank(uid, group, target, args.Here))
            args.Facts |= CFGOAPFact.AtFlank;
        else if (!_steering.IsWalkable(uid, _encounters.SlotAt(uid, group, target)))
            args.Facts |= CFGOAPFact.FlankBlocked;
    }

    private bool HasAlliesNearby(EntityUid uid, MapCoordinates here)
    {
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(here, AllyTiles))
        {
            if (mob.Owner != uid && _mobState.IsAlive(mob) && !_factions.CanHarm(uid, mob))
                return true;
        }

        return false;
    }
}
