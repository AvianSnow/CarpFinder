using Content.Shared.ActionBlocker;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusIcon;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Applies the Pathfinder 2e off-guard condition while a creature is flanked.
/// </summary>
public sealed partial class CFFlankingSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFDyingSystem _dying = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedStatusIconSystem _statusIcons = default!;

    private readonly List<EntityUid> _affected = new();
    private readonly List<FlankCandidate> _candidates = new();
    private readonly HashSet<EntityUid> _flankers = new();

    [SubscribeLocalEvent]
    private void OnMove(Entity<MobStateComponent> ent, ref MoveEvent args)
    {
        if (!_net.IsServer || args.OnlyRotation)
            return;

        if (TryGetTile(args.OldPosition, out var oldSpace)
            && TryGetTile(args.NewPosition, out var newSpace)
            && oldSpace.Equals(newSpace))
            return;

        RefreshAround(ent.Owner);
    }

    // By-value broadcast. The directed MobStateComponent subscription is already taken by stun,
    // and the existing broadcast handlers are by-value.
    [SubscribeLocalEvent]
    private void OnMobState(MobStateChangedEvent args)
    {
        if (!_net.IsServer)
            return;

        RefreshAround(args.Target);
    }

    [SubscribeLocalEvent]
    private void OnEquip(Entity<MobStateComponent> ent, ref DidEquipHandEvent args)
    {
        if (!_net.IsServer)
            return;

        RefreshAround(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnUnequip(Entity<MobStateComponent> ent, ref DidUnequipHandEvent args)
    {
        if (!_net.IsServer)
            return;

        RefreshAround(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnReachWeaponInit(Entity<CFMeleeWeaponComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.ReachFeet <= CFFlanking.DefaultReachFeet)
            return;

        if (!TryComp(ent.Owner, out MeleeWeaponComponent? melee))
            return;

        var range = CFFlanking.Ss14SwingRange(ent.Comp.ReachFeet);
        if (melee.Range >= range)
            return;

        melee.Range = range;
        Dirty(ent.Owner, melee);
    }

    [SubscribeLocalEvent]
    private void OnExamine(Entity<CFOffGuardComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.Active)
            return;

        args.PushMarkup(Loc.GetString("cf-combat-off-guard-examine"));
    }

    /// <summary>
    /// True when <paramref name="attacker"/> is flanking <paramref name="target"/> with an ally.
    /// Refreshes the off-guard condition as a side effect.
    /// </summary>
    public bool IsOffGuardTo(EntityUid target, EntityUid attacker)
    {
        if (!_net.IsServer)
            return false;

        Refresh(target);
        return TryComp(target, out CFOffGuardComponent? comp) && comp.FlankedBy.Contains(attacker);
    }

    private void RefreshAround(EntityUid uid)
    {
        _affected.Clear();
        _affected.Add(uid);

        if (TryComp(uid, out TransformComponent? xform))
        {
            foreach (var other in _lookup.GetEntitiesInRange<MobStateComponent>(xform.Coordinates, CFFlanking.SearchRange))
            {
                if (other.Owner != uid)
                    _affected.Add(other.Owner);
            }
        }

        foreach (var id in _affected)
            Refresh(id);
    }

    private void Refresh(EntityUid target)
    {
        if (!TryComp(target, out MobStateComponent? _))
        {
            Clear(target);
            return;
        }

        CollectFlankers(target, _candidates);
        _flankers.Clear();
        for (var i = 0; i < _candidates.Count; i++)
        {
            for (var j = i + 1; j < _candidates.Count; j++)
            {
                var a = _candidates[i];
                var b = _candidates[j];
                if (!AreAllies(a.Uid, b.Uid))
                    continue;

                if (!CFFlanking.CentersFlank(a.Tile, b.Tile, a.TargetTile))
                    continue;

                _flankers.Add(a.Uid);
                _flankers.Add(b.Uid);
            }
        }

        if (_flankers.Count == 0)
        {
            Clear(target);
            return;
        }

        var comp = EnsureComp<CFOffGuardComponent>(target);
        var wasActive = comp.Active;
        comp.FlankedBy.Clear();
        foreach (var flanker in _flankers)
            comp.FlankedBy.Add(flanker);

        comp.Active = true;
        EnsureFlankIcon(target);
        if (wasActive)
            return;

        Dirty(target, comp);
        _popup.PopupEntity(Loc.GetString("cf-combat-off-guard"), target, PopupType.Medium);
    }

    private void Clear(EntityUid target)
    {
        var wasActive = false;
        if (TryComp(target, out CFOffGuardComponent? comp))
        {
            wasActive = comp.Active;
            RemComp<CFOffGuardComponent>(target);
        }

        RemoveFlankIcon(target);
        if (wasActive)
            _popup.PopupEntity(Loc.GetString("cf-combat-off-guard-cleared"), target, PopupType.Small);
    }

    private void EnsureFlankIcon(EntityUid uid)
    {
        if (_statusIcons.CFTryAddStatusIcon(uid))
            EnsureComp<CFAddedStatusIconComponent>(uid);
    }

    private void RemoveFlankIcon(EntityUid uid)
    {
        if (!HasComp<CFAddedStatusIconComponent>(uid))
            return;

        RemComp<CFAddedStatusIconComponent>(uid);
        _statusIcons.CFRemoveStatusIcon(uid);
    }

    private void CollectFlankers(EntityUid target, List<FlankCandidate> candidates)
    {
        candidates.Clear();
        if (!TryGetTile(Transform(target).Coordinates, out var targetSpace))
            return;

        foreach (var other in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(target).Coordinates, CFFlanking.SearchRange))
        {
            var uid = other.Owner;
            if (uid == target || !CanFlank(uid))
                continue;

            if (AreAllies(uid, target))
                continue;

            if (!TryGetTile(Transform(uid).Coordinates, out var space) || !targetSpace.SameSpace(space))
                continue;

            var reach = GetMeleeReachFeet(uid);
            if (!CFFlanking.InReach(space.Tile, targetSpace.Tile, reach))
                continue;

            candidates.Add(new FlankCandidate(uid, space.Tile, targetSpace.Tile));
        }
    }

    private bool CanFlank(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return false;

        if (TryComp(uid, out MobStateComponent? state) && state.CurrentState != MobState.Alive)
            return false;

        if (TryComp(uid, out CFCombatantComponent? combat) && _dying.IsDown(combat))
            return false;

        if (_container.IsEntityInContainer(uid))
            return false;

        // Stunned, pacified, and other effects that cancel attacks.
        if (!_blocker.CanAttack(uid))
            return false;

        return GetMeleeReachFeet(uid) > 0;
    }

    private int GetMeleeReachFeet(EntityUid uid)
    {
        var best = 0;
        if (_hands.GetHandCount(uid) > 0)
        {
            foreach (var held in _hands.EnumerateHeld(uid))
            {
                if (TryComp(held, out CFMeleeWeaponComponent? weapon))
                    best = Math.Max(best, weapon.ReachFeet);
                else if (HasComp<MeleeWeaponComponent>(held) && !HasComp<CFRangedWeaponComponent>(held))
                    best = Math.Max(best, CFFlanking.DefaultReachFeet);
            }

            if (_hands.CountFreeHands(uid) > 0)
                best = Math.Max(best, CFFlanking.DefaultReachFeet);
        }
        else if (TryComp(uid, out CFMeleeWeaponComponent? body))
        {
            best = Math.Max(best, body.ReachFeet);
        }
        else if (HasComp<MeleeWeaponComponent>(uid))
        {
            best = CFFlanking.DefaultReachFeet;
        }

        return best;
    }

    /// <summary>
    /// Allies share a faction, or are two player characters with no NPC faction.
    /// A player and a factioned mob are not allies, so they do not flank together.
    /// </summary>
    private bool AreAllies(EntityUid a, EntityUid b)
    {
        if (a == b)
            return false;

        if (_factions.IsEntityFriendly(a, b) || _factions.IsEntityFriendly(b, a))
            return true;

        // IsEntityFriendly(self, self) is true only when the entity belongs to a faction.
        var aFaction = _factions.IsEntityFriendly(a, a);
        var bFaction = _factions.IsEntityFriendly(b, b);
        return !aFaction && !bFaction && HasComp<ActorComponent>(a) && HasComp<ActorComponent>(b);
    }

    private bool TryGetTile(EntityCoordinates coordinates, out TileSpace space)
    {
        space = default;
        if (!coordinates.IsValid(EntityManager))
            return false;

        var mapCoords = _transform.ToMapCoordinates(coordinates);
        if (mapCoords.MapId == MapId.Nullspace)
            return false;

        var parent = coordinates.EntityId;
        EntityUid? gridId = null;
        if (HasComp<MapGridComponent>(parent))
            gridId = parent;
        else if (TryComp(parent, out TransformComponent? parentXform))
            gridId = parentXform.GridUid;

        if (gridId is { } grid && TryComp(grid, out MapGridComponent? gridComp))
        {
            space = new TileSpace(grid, mapCoords.MapId, _map.WorldToTile(grid, gridComp, mapCoords.Position));
            return true;
        }

        var floored = new Vector2i((int)MathF.Floor(mapCoords.X), (int)MathF.Floor(mapCoords.Y));
        space = new TileSpace(EntityUid.Invalid, mapCoords.MapId, floored);
        return true;
    }

    private readonly record struct FlankCandidate(EntityUid Uid, Vector2i Tile, Vector2i TargetTile);

    private readonly record struct TileSpace(EntityUid Grid, MapId Map, Vector2i Tile)
    {
        public bool SameSpace(TileSpace other)
        {
            if (Map != other.Map)
                return false;

            if (Grid.IsValid() || other.Grid.IsValid())
                return Grid == other.Grid;

            return true;
        }
    }
}
