using Content.Shared.ActionBlocker;
using Content.Shared.CF.Factions;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusIcon;
using Content.Shared.Weapons.Melee;
using System.Numerics;
using Robust.Shared.Containers;
using Robust.Shared.Map;
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
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private CFFactionSystem _cfFactions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFDyingSystem _dying = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedStatusIconSystem _statusIcons = default!;

    private readonly List<EntityUid> _affected = new();
    private readonly List<EntityUid> _refreshSnapshot = new();
    private readonly List<FlankCandidate> _candidates = new();
    private readonly HashSet<EntityUid> _flankers = new();
    private readonly HashSet<EntityUid> _moveRefresh = new();

    public override void Update(float frameTime)
    {
        if (!_net.IsServer || _moveRefresh.Count == 0)
            return;

        _affected.Clear();
        foreach (var uid in _moveRefresh)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            _affected.Add(uid);
            if (!TryComp(uid, out TransformComponent? xform))
                continue;

            foreach (var other in _lookup.GetEntitiesInRange<MobStateComponent>(xform.Coordinates, CFFlanking.SearchRange))
                _affected.Add(other.Owner);
        }

        _moveRefresh.Clear();
        _refreshSnapshot.Clear();
        _refreshSnapshot.AddRange(_affected);

        foreach (var id in _refreshSnapshot)
        {
            if (!TerminatingOrDeleted(id))
                Refresh(id);
        }
    }

    [SubscribeLocalEvent]
    private void OnMove(Entity<MobStateComponent> ent, ref MoveEvent args)
    {
        if (!_net.IsServer || args.OnlyRotation)
            return;

        // A dying or dead body keeps sliding in the physics step. That must not schedule flanking.
        // Dying recovery itself already runs once per game tick in CFDyingSystem.Update.
        if (IsDeadOrDying(ent.Owner, ent.Comp))
            return;

        // One flanking pass per tick, not one per physics step.
        _moveRefresh.Add(ent.Owner);
    }

    // By-value broadcast. The directed MobStateComponent subscription is already taken by stun,
    // and the existing broadcast handlers are by-value.
    [SubscribeLocalEvent]
    private void OnRevived(Entity<CFCombatantComponent> ent, ref CFCombatantRevivedEvent args)
    {
        if (!_net.IsServer)
            return;

        RefreshAround(ent.Owner);
    }

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
        if (!TryComp(target, out MobStateComponent? state) || IsDeadOrDying(target, state))
        {
            Clear(target, announce: false);
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

                if (!CFFlanking.LineFlanks(a.Position, b.Position, a.TargetPosition))
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

        if (TryComp(target, out CFOffGuardComponent? existing)
            && existing.Active
            && existing.FlankedBy.SetEquals(_flankers))
            return;

        var comp = existing ?? EnsureComp<CFOffGuardComponent>(target);
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

    private void Clear(EntityUid target, bool announce = true)
    {
        var wasActive = false;
        if (TryComp(target, out CFOffGuardComponent? comp))
        {
            wasActive = comp.Active;
            RemComp<CFOffGuardComponent>(target);
        }

        RemoveFlankIcon(target);
        if (wasActive && announce)
            _popup.PopupEntity(Loc.GetString("cf-combat-off-guard-cleared"), target, PopupType.Small);
    }

    /// <summary>
    /// Dead and dying creatures do not take the flanking off-guard condition.
    /// </summary>
    private bool IsDeadOrDying(EntityUid uid, MobStateComponent state)
    {
        if (state.CurrentState != MobState.Alive)
            return true;

        return TryComp(uid, out CFCombatantComponent? combat)
            && (_dying.IsDying(combat) || _dying.IsDown(combat));
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
        if (!TryGetWorld(target, out var targetPos))
            return;

        foreach (var other in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(target).Coordinates, CFFlanking.SearchRange))
        {
            var uid = other.Owner;
            if (uid == target || !CanFlank(uid))
                continue;

            if (AreAllies(uid, target))
                continue;

            if (!TryGetWorld(uid, out var pos) || pos.MapId != targetPos.MapId)
                continue;

            var reach = GetMeleeReachFeet(uid);
            if (!CFFlanking.InReach(pos.Position, targetPos.Position, reach))
                continue;

            candidates.Add(new FlankCandidate(uid, pos.Position, targetPos.Position));
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
    /// Party members flank together. Creatures with no CF faction fall back to the stock NPC faction,
    /// or two player characters with no NPC faction.
    /// </summary>
    private bool AreAllies(EntityUid a, EntityUid b)
    {
        if (a == b)
            return false;

        if (HasComp<CFFactionComponent>(a) && HasComp<CFFactionComponent>(b))
            return _cfFactions.GetAlliance(a, b) == CFAlliance.Party;

        if (_factions.IsEntityFriendly(a, b) || _factions.IsEntityFriendly(b, a))
            return true;

        // IsEntityFriendly(self, self) is true only when the entity belongs to a faction.
        var aFaction = _factions.IsEntityFriendly(a, a);
        var bFaction = _factions.IsEntityFriendly(b, b);
        return !aFaction && !bFaction && HasComp<ActorComponent>(a) && HasComp<ActorComponent>(b);
    }

    private bool TryGetWorld(EntityUid uid, out MapCoordinates coords)
    {
        coords = default;
        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        coords = _transform.GetMapCoordinates(uid, xform);
        return coords.MapId != MapId.Nullspace;
    }

    private readonly record struct FlankCandidate(EntityUid Uid, Vector2 Position, Vector2 TargetPosition);
}
