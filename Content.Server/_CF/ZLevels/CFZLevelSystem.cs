using Content.Server.Station.Events;
using Content.Shared.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Actions;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.ZLevels;
using Content.Shared.GameTicking.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Station.Components;
using Content.Shared.Station.Systems;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Content.Shared.Throwing;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server.CF.ZLevels;

/// <summary>
/// Stacks maps into levels and moves creatures between them: stairs walk through a portal, ladders Climb,
/// and holes fall. Based on CP14's <c>CP14StationZLevelsSystem</c>.
/// </summary>
public sealed partial class CFZLevelSystem : EntitySystem
{
    /// <summary>How close to a hole's center a landing has to be to drop in.</summary>
    private const float HoleLandRange = 0.5f;

    [Dependency] private CFActionPoolSystem _actionPool = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private LinkedEntitySystem _links = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StationSystem _station = default!;

    [SubscribeLocalEvent]
    private void OnStationPostInit(Entity<CFStationZLevelsComponent> ent, ref StationPostInitEvent args)
    {
        if (_station.GetLargestGrid(ent.Owner) is not { } grid)
        {
            Log.Error($"Z-level station {ToPrettyString(ent)} has no grid.");
            return;
        }

        ent.Comp.Maps[Transform(grid).MapID] = ent.Comp.DefaultLevel;

        foreach (var (level, path) in ent.Comp.Levels)
        {
            if (!_loader.TryLoadMap(path, out var map, out _))
            {
                Log.Error($"Could not load level {level} from {path}.");
                continue;
            }

            ent.Comp.Maps[map.Value.Comp.MapId] = level;
            _maps.InitializeMap(map.Value.Comp.MapId);
            EnsureComp<StationMemberComponent>(map.Value).Station = ent;
        }

        // Portals on the first map initialized before the other levels existed.
        LinkWaitingPortals();
    }

    /// <summary>Joins maps into a network, from the lowest level up. Returns the network.</summary>
    public Entity<CFStationZLevelsComponent> Combine(IReadOnlyList<MapId> maps)
    {
        var uid = Spawn();
        var network = EnsureComp<CFStationZLevelsComponent>(uid);
        for (var i = 0; i < maps.Count; i++)
        {
            network.Maps[maps[i]] = i;
        }

        LinkWaitingPortals();
        return (uid, network);
    }

    public bool InNetwork(MapId map)
    {
        return FindNetwork(map) != null;
    }

    private Entity<CFStationZLevelsComponent>? FindNetwork(MapId map)
    {
        var networks = AllEntityQuery<CFStationZLevelsComponent>();
        while (networks.MoveNext(out var uid, out var network))
        {
            if (network.Maps.ContainsKey(map))
                return (uid, network);
        }

        return null;
    }

    /// <summary>The map <paramref name="offset"/> levels from this one, if there is one.</summary>
    public MapId? MapAtOffset(MapId map, int offset)
    {
        if (FindNetwork(map) is not { } network)
            return null;

        var target = network.Comp.Maps[map] + offset;
        foreach (var (id, level) in network.Comp.Maps)
        {
            if (level == target && _maps.MapExists(id))
                return id;
        }

        return null;
    }

    [SubscribeLocalEvent]
    private void OnPortalInit(Entity<CFZLevelAutoPortalComponent> ent, ref MapInitEvent args)
    {
        TryLinkPortal(ent);
    }

    [SubscribeLocalEvent]
    private void OnRoundStarting(RoundStartingEvent args)
    {
        LinkWaitingPortals();
    }

    private void LinkWaitingPortals()
    {
        // Includes paused maps: the station's own map is still paused while its levels load.
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
        if (MapAtOffset(xform.MapID, ent.Comp.Offset) is not { } target)
            return;

        var other = Spawn(ent.Comp.OtherSide, new MapCoordinates(_transform.GetWorldPosition(xform), target));

        // Removed at once, so the portal's own map init cannot build a second end.
        if (_links.TryLink(ent, other, true))
            RemComp<CFZLevelAutoPortalComponent>(ent);
    }

    [SubscribeLocalEvent]
    private void OnHoleCollide(Entity<CFZLevelHoleComponent> ent, ref StartCollideEvent args)
    {
        // A creature in the air clears it. Landing in one is below.
        if (HasComp<MobStateComponent>(args.OtherEntity) && args.OtherBody.BodyStatus != BodyStatus.InAir)
            Fall(args.OtherEntity);
    }

    /// <summary>Contact started while it was in the air, so nothing else would drop a creature landing in a hole.</summary>
    [SubscribeLocalEvent]
    private void OnCreatureLand(Entity<CFCombatantComponent> ent, ref LandEvent args)
    {
        if (_lookup.GetEntitiesInRange<CFZLevelHoleComponent>(_transform.GetMapCoordinates(ent), HoleLandRange).Count > 0)
            Fall(ent);
    }

    /// <summary>
    /// Drops a creature to the next level with floor under it, through any levels that are open air.
    /// Returns the distance in feet, or 0 if there is nothing below.
    /// </summary>
    public int Fall(EntityUid creature)
    {
        var xform = Transform(creature);
        var position = _transform.GetWorldPosition(xform);
        var map = xform.MapID;
        if (FindNetwork(map) is not { } network)
            return 0;

        var feet = 0;
        while (MapAtOffset(map, -1) is { } below)
        {
            map = below;
            feet += network.Comp.LevelHeightFeet;
            if (HasFloor(map, position))
                break;
        }

        if (feet == 0)
            return 0;

        _transform.SetMapCoordinates(creature, new MapCoordinates(position, map));

        var damage = FallsSafely(creature) ? 0 : CFFallRules.Damage(feet);
        if (damage > 0)
        {
            _effects.DealDamage(creature, creature, damage, CFDamageTypes.Bludgeoning, false);
            _conditions.KnockProne(creature);
        }

        return feet;
    }

    public bool FallsSafely(EntityUid creature)
    {
        return TryComp(creature, out CFSafeFallComponent? safe)
               && (!safe.NeedsFreeHand || _hands.CountFreeHands(creature) > 0);
    }

    private bool HasFloor(MapId map, System.Numerics.Vector2 position)
    {
        if (!_maps.TryFindGridAt(_maps.GetMapOrInvalid(map), position, out var uid, out var grid))
            return false;

        return _maps.TryGetTileRef(uid, grid, _maps.WorldToTile(uid, grid, position), out var tile) && !tile.Tile.IsEmpty;
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

        var goingDown = false;
        EntityUid? destination = null;
        foreach (var other in link.LinkedEntities)
        {
            destination = other;
            goingDown = LevelOf(Transform(other).MapID) < LevelOf(Transform(ent).MapID);
            break;
        }

        if (destination is not { } end)
            return false;

        if (ent.Comp.Dc > 0)
        {
            var degree = _conditions.RollSkillAgainst(user, "Athletics", CFAbility.Strength, ent.Comp.Dc, out _);
            if (degree == CFDegree.CriticalFailure)
            {
                if (goingDown)
                    Fall(user);
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

    private int LevelOf(MapId map)
    {
        return FindNetwork(map) is { } network && network.Comp.Maps.TryGetValue(map, out var level) ? level : 0;
    }

    [SubscribeLocalEvent]
    private void OnMoverInit(Entity<CFZLevelMoverComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.UpActionEntity, ent.Comp.UpAction);
        _actions.AddAction(ent, ref ent.Comp.DownActionEntity, ent.Comp.DownAction);
        Dirty(ent);
    }

    [SubscribeLocalEvent]
    private void OnMoverRemove(Entity<CFZLevelMoverComponent> ent, ref ComponentRemove args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.UpActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.DownActionEntity);
    }

    [SubscribeLocalEvent]
    private void OnMoveUp(Entity<CFZLevelMoverComponent> ent, ref CFZLevelActionUp args)
    {
        args.Handled = true;
        Shift(ent, 1);
    }

    [SubscribeLocalEvent]
    private void OnMoveDown(Entity<CFZLevelMoverComponent> ent, ref CFZLevelActionDown args)
    {
        args.Handled = true;
        Shift(ent, -1);
    }

    /// <summary>Moves straight up or down a level, for ghosts and admins.</summary>
    public bool Shift(EntityUid uid, int offset)
    {
        var xform = Transform(uid);
        if (MapAtOffset(xform.MapID, offset) is not { } target)
            return false;

        _transform.SetMapCoordinates(uid, new MapCoordinates(_transform.GetWorldPosition(xform), target));
        return true;
    }
}
