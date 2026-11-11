using System.Numerics;
using Content.Server.CF.GOAP.Encounters;
using Content.Server.CF.GOAP.Steering;
using Content.Server.CF.GOAP.Tactics;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Factions;
using Content.Shared.CF.GOAP;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server.CF.GOAP.Sensors;

/// <summary>
/// What it can see. This runs before the other sensors, because it decides who the creature is after and they
/// read that.
/// </summary>
public sealed partial class CFGOAPSightSensorSystem : EntitySystem
{
    private const float SightTiles = 12f;

    private const float ReachTiles = 1.25f;

    /// <summary>The creature a companion's master pointed at beats any score tactics give, while it can be seen.</summary>
    private const float PointedScore = 1_000_000f;

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFGOAPEncounterSystem _encounters = default!;
    [Dependency] private CFGOAPSteeringSystem _steering = default!;
    [Dependency] private CFGOAPTacticsSystem _tactics = default!;
    [Dependency] private CFStealthSystem _stealth = default!;

    [SubscribeLocalEvent]
    private void OnSense(ref CFGOAPSenseEvent args)
    {
        var uid = args.Creature.Owner;
        if (!TryFindBestEnemy(args.Creature, args.Here, out var seen) && !TryGetFelt(uid, out seen))
            return;

        seen = PackTarget(uid, seen);
        Follow(args.Creature, seen);
        args.Facts |= CFGOAPFact.TargetVisible;

        // Glass lets it see the target without being able to touch it. A table is no such thing: a swing goes over it.
        if ((_transform.GetMapCoordinates(seen).Position - args.Here.Position).Length() <= ReachTiles
            && _interaction.InRangeUnobstructed(uid, seen, ReachTiles))
            args.Facts |= CFGOAPFact.TargetInReach;
    }

    /// <summary>Makes this the creature it is after, and starts a new search from where it is now.</summary>
    public void Follow(Entity<CFGOAPComponent> ent, EntityUid target)
    {
        ent.Comp.Target = target;
        ent.Comp.NodesChecked = 0;
        ent.Comp.Lkp = _transform.GetMapCoordinates(target);
        ent.Comp.LkpReached = false;
        ent.Comp.LkpHeading = CompOrNull<PhysicsComponent>(target)?.LinearVelocity ?? Vector2.Zero;
    }

    /// <summary>An enemy its rays touched, which they only do in plain view.</summary>
    private bool TryGetFelt(EntityUid uid, out EntityUid felt)
    {
        felt = CompOrNull<CFGOAPSteeringComponent>(uid)?.Felt ?? default;
        return felt.IsValid() && !TerminatingOrDeleted(felt);
    }

    /// <summary>
    /// The enemy it can really see that its tactics rate highest. An undetected creature is not seen, however close it
    /// is. Sight is checked last, because it costs the most and most enemies are already ruled out by then.
    /// </summary>
    private bool TryFindBestEnemy(Entity<CFGOAPComponent> ent, MapCoordinates here, out EntityUid target)
    {
        target = default;
        var uid = ent.Owner;
        var best = float.MinValue;
        var companion = CompOrNull<CFCompanionComponent>(uid);
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(here, SightTiles))
        {
            if (mob.Owner == uid
                || !_mobState.IsAlive(mob)
                || !_factions.CanHarm(uid, mob)
                || _stealth.Detection(mob, uid) == CFDetection.Undetected)
                continue;

            // A companion goes for real enemies by itself, and for anything else only when its master points at it.
            var pointedAt = companion != null && mob.Owner == companion.Target;
            if (companion != null && !pointedAt && _factions.GetAlliance(uid, mob) != CFAlliance.Opposition)
                continue;

            var distance = (_transform.GetMapCoordinates(mob).Position - here.Position).Length();
            var score = pointedAt ? PointedScore : _tactics.ScoreTarget(ent, mob, distance);
            if (score <= best || !_examine.InRangeUnOccluded(uid, mob, SightTiles))
                continue;

            best = score;
            target = mob;
        }

        return best > float.MinValue;
    }

    /// <summary>
    /// The group goes for one creature, so it can surround it. A member only joins in on a target it can see itself.
    /// </summary>
    private EntityUid PackTarget(EntityUid uid, EntityUid seen)
    {
        if (!_encounters.TryGetGroup(uid, out var group))
            return seen;

        if (group.Target is { } shared && !TerminatingOrDeleted(shared) && _mobState.IsAlive(shared))
        {
            return _stealth.Detection(shared, uid) != CFDetection.Undetected && _examine.InRangeUnOccluded(uid, shared, SightTiles)
                ? shared
                : seen;
        }

        group.Target = seen;
        group.FlankAngle = _random.NextFloat(0f, MathF.Tau);
        return seen;
    }
}
