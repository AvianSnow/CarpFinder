using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Server.CF.ZLevels.Core;
using Content.Shared.CF.ZLevels;
using Content.Shared.CF.ZLevels.Core;
using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Content.Shared.GameTicking.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server.CF.ZLevels.Portals;

/// <summary>
/// The ways between levels kept from the CP14 port, on top of the CrystallEdge network: stairs are a portal whose
/// other end is built on the next level, a ladder is clicked to Climb, and a hole drops whoever steps on it.
/// </summary>
public sealed partial class CFZLevelPortalSystem : EntitySystem
{
    /// <summary>Just under the ceiling, so a dropped creature falls the whole level.</summary>
    private const float DropHeight = 0.95f;

    /// <summary>How close to a hole's center a landing has to be to drop in.</summary>
    private const float HoleLandRange = 0.5f;

    [Dependency] private CFActionPoolSystem _actionPool = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFSharedZLevelsSystem _zLevels = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private LinkedEntitySystem _links = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent]
    private void OnPortalInit(Entity<CFZLevelAutoPortalComponent> ent, ref MapInitEvent args)
    {
        TryLinkPortal(ent);
    }

    /// <summary>The station's own map is initialized before its other levels join the network.</summary>
    [SubscribeLocalEvent]
    private void OnNetworkUpdated(CFZLevelMapNetworkUpdatedEvent args)
    {
        LinkWaitingPortals();
    }

    [SubscribeLocalEvent]
    private void OnRoundStarting(RoundStartingEvent args)
    {
        LinkWaitingPortals();
    }

    private void LinkWaitingPortals()
    {
        // Includes paused maps: a level can still be loading while the others are ready.
        var waiting = new List<Entity<CFZLevelAutoPortalComponent>>();
        var query = AllEntityQuery<CFZLevelAutoPortalComponent>();
        while (query.MoveNext(out var uid, out var portal))
        {
            waiting.Add((uid, portal));
        }

        foreach (var portal in waiting)
        {
            TryLinkPortal(portal);
        }
    }

    private void TryLinkPortal(Entity<CFZLevelAutoPortalComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.MapUid is not { } map || !_zLevels.TryMapOffset(map, ent.Comp.Offset, out _, out var targetMap))
            return;

        var other = Spawn(ent.Comp.OtherSide, new MapCoordinates(_transform.GetWorldPosition(xform), targetMap.MapId));

        // Removed at once, so the portal's own map init cannot build a second end.
        if (_links.TryLink(ent, other, true))
            RemComp<CFZLevelAutoPortalComponent>(ent);
    }

    [SubscribeLocalEvent]
    private void OnHoleCollide(Entity<CFZLevelHoleComponent> ent, ref StartCollideEvent args)
    {
        // A creature in the air clears it. Landing in one is below.
        if (HasComp<MobStateComponent>(args.OtherEntity) && args.OtherBody.BodyStatus != BodyStatus.InAir)
            Drop(args.OtherEntity);
    }

    /// <summary>Contact started while it was in the air, so nothing else would drop a creature landing in a hole.</summary>
    [SubscribeLocalEvent]
    private void OnCreatureLand(Entity<CFCombatantComponent> ent, ref LandEvent args)
    {
        if (_lookup.GetEntitiesInRange<CFZLevelHoleComponent>(_transform.GetMapCoordinates(ent), HoleLandRange).Count > 0)
            Drop(ent);
    }

    /// <summary>
    /// Puts a creature on the level below, just under the ceiling. Vertical physics brings it down from there,
    /// and the landing deals the fall's damage. Returns false with no level below.
    /// </summary>
    public bool Drop(EntityUid creature)
    {
        if (!_zLevels.TryMoveDown(creature))
            return false;

        if (HasComp<CFZPhysicsComponent>(creature))
            _zLevels.SetZPosition(creature, DropHeight);

        return true;
    }

    [SubscribeLocalEvent]
    private void OnClimbInteract(Entity<CFZLevelClimbComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Climb(ent, args.User);
    }

    /// <summary>
    /// Climb: needs two free hands and pays its action. With a DC, Athletics decides. A failure stays put,
    /// and a critical failure falls: from the top that hurts, and from the bottom it leaves you prone.
    /// Returns true if the creature reached the other end.
    /// </summary>
    public bool Climb(Entity<CFZLevelClimbComponent> ent, EntityUid user)
    {
        if (!TryComp(ent, out LinkedEntityComponent? link) || link.LinkedEntities.Count == 0)
            return false;

        foreach (var _ in _hands.EnumerateHeld(user))
        {
            _popup.PopupEntity(Loc.GetString("cf-zlevel-hands-full"), ent, user);
            return false;
        }

        if (!_actionPool.TrySpendActions(user, ent.Comp.Actions))
            return false;

        EntityUid? destination = null;
        foreach (var other in link.LinkedEntities)
        {
            destination = other;
            break;
        }

        if (destination is not { } end)
            return false;

        var goingDown = Depth(end) < Depth(ent);
        var options = CompOrNull<CFCombatOptionsComponent>(user);
        if (ent.Comp.Dc > 0 && options is not { ClimbSpeed: true })
        {
            var degree = _conditions.RollSkillAgainst(user, "Athletics", CFAbility.Strength, ent.Comp.Dc, out _, itemBonus: options?.ClimbBonus ?? 0);
            if (degree == CFDegree.Success && options is { ClimbSuccessCritical: true })
                degree = CFDegree.CriticalSuccess;

            if (degree == CFDegree.CriticalFailure)
            {
                if (goingDown)
                    Drop(user);
                else
                    _conditions.KnockProne(user);

                return false;
            }

            if (!degree.IsSuccess())
            {
                _popup.PopupEntity(Loc.GetString("cf-zlevel-climb-failed"), ent, user);
                return false;
            }
        }

        _transform.SetCoordinates(user, Transform(end).Coordinates);
        return true;
    }

    private int Depth(EntityUid uid)
    {
        return TryComp(Transform(uid).MapUid, out CFZMapComponent? map) ? map.Depth : 0;
    }
}
