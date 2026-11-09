using Robust.Shared.Prototypes;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Combat;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Factions;
using Content.Shared.CombatMode;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Pointing;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server.CF.Companions;

/// <summary>
/// Points the summoner's allies at a creature, sends them to fetch an item, calls them back when the summoner points at themself,
/// or calls them off when the point is the floor.
/// The ally closes in and Strikes with its own stat block. A ghost in the body is left in control.
/// </summary>
public sealed partial class CFCompanionSystem : EntitySystem
{
    private const float BindRange = 4f;
    private const float FollowRange = 1.5f;
    private const float PickupRange = 0.6f;
    private const int FetchGiveUp = 45;

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFFactionSystem _factions = default!;

    private readonly List<(EntityUid Companion, EntityUid Item)> _pendingGives = new();
    private readonly HashSet<EntityUid> _thrown = new();
    private readonly HashSet<EntityUid> _leaveOnGround = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CFCompanionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CFCompanionComponent, DidUnequipHandEvent>(OnUnequip);
        SubscribeLocalEvent<CFCompanionComponent, BeforeThrowEvent>(OnBeforeThrow);
        SubscribeLocalEvent<CFSummonerComponent, AfterPointedAtEvent>(OnPointed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        DeliverDrops();

        var query = EntityQueryEnumerator<CFCompanionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Fetch.IsValid())
            {
                UpdateFetch(uid, comp);
                continue;
            }

            if (comp.Target.IsValid())
            {
                if (!Exists(comp.Target) || !IsLiving(comp.Target))
                {
                    Stop(uid, comp);
                    continue;
                }

                if (!HasMind(uid) && !HasComp<NPCMeleeCombatComponent>(uid))
                    Order(uid, comp, comp.Target);

                continue;
            }

            if (!comp.Following)
                continue;

            if (!comp.Summoner.IsValid() || !Exists(comp.Summoner) || !IsLiving(comp.Summoner))
            {
                Stop(uid, comp);
                continue;
            }

            if (!HasMind(uid) && !HasComp<NPCSteeringComponent>(uid))
                Follow(uid, comp);
        }
    }

    private void OnBeforeThrow(Entity<CFCompanionComponent> ent, ref BeforeThrowEvent args)
    {
        _thrown.Add(args.ItemUid);
    }

    private void OnUnequip(EntityUid uid, CFCompanionComponent comp, DidUnequipHandEvent args)
    {
        _pendingGives.Add((uid, args.Unequipped));
    }

    /// <summary>
    /// A drop beside the summoner goes into their hands. A throw stays a throw.
    /// </summary>
    private void DeliverDrops()
    {
        foreach (var (companion, item) in _pendingGives)
        {
            if (_thrown.Contains(item) || HasComp<ThrownItemComponent>(item))
                continue;

            if (_leaveOnGround.Remove(item))
                continue;

            if (!Exists(companion) || !Exists(item) || _containers.IsEntityInContainer(item))
                continue;

            if (!TryComp(companion, out CFCompanionComponent? comp)
                || !comp.Summoner.IsValid()
                || !Exists(comp.Summoner)
                || !TryComp(item, out ItemComponent? itemComp))
                continue;

            if (!Transform(companion).Coordinates.TryDistance(EntityManager, Transform(comp.Summoner).Coordinates, out var distance)
                || distance > SharedInteractionSystem.InteractionRange)
                continue;

            if (_hands.TryPickupAnyHand(comp.Summoner, item, checkActionBlocker: true, item: itemComp))
            {
                _popup.PopupEntity(Loc.GetString("cf-companion-give"), comp.Summoner, comp.Summoner);
                continue;
            }

            _transform.PlaceNextTo(item, comp.Summoner);
            _popup.PopupEntity(Loc.GetString("cf-companion-give-ground"), comp.Summoner, comp.Summoner);
        }

        _pendingGives.Clear();
        _thrown.Clear();
    }

    private void OnMapInit(Entity<CFCompanionComponent> ent, ref MapInitEvent args)
    {
        _actions.Ensure(ent);

        if (ent.Comp.Summoner.IsValid() && Exists(ent.Comp.Summoner) && ent.Comp.Summoner != ent.Owner)
        {
            Bind(ent.Comp.Summoner, ent);
            return;
        }

        if (FindSummoner(ent) is { } nearest)
            Bind(nearest, ent);
    }

    /// <summary>Sends a companion at a creature.</summary>
    public void OrderAttack(EntityUid companion, EntityUid target)
    {
        if (TryComp(companion, out CFCompanionComponent? comp))
            Order(companion, comp, target);
    }

    /// <summary>Makes a companion stop what it was ordered to do.</summary>
    public void StopOrder(EntityUid companion)
    {
        if (TryComp(companion, out CFCompanionComponent? comp))
            Stop(companion, comp);
    }

    private void OnPointed(Entity<CFSummonerComponent> ent, ref AfterPointedAtEvent args)
    {
        var attack = IsOrder(ent.Owner, args.Pointed);
        var changed = false;
        var any = false;

        var query = EntityQueryEnumerator<CFCompanionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // A thrall only ever acts when commanded.
            if (comp.Summoner != ent.Owner || HasComp<CFThrallComponent>(uid))
                continue;

            any = true;
            if (args.Pointed == ent.Owner)
            {
                if (comp.Following && !comp.Target.IsValid() && !comp.Fetch.IsValid())
                    continue;

                Follow(uid, comp);
                changed = true;
            }
            else if (attack && args.Pointed != uid)
            {
                if (comp.Target == args.Pointed)
                    continue;

                Order(uid, comp, args.Pointed);
                changed = true;
            }
            else if (IsFetchable(args.Pointed))
            {
                if (comp.Fetch == args.Pointed)
                    continue;

                Fetch(uid, comp, args.Pointed);
                changed = true;
            }
            else if (comp.Target.IsValid() || comp.Following || comp.Fetch.IsValid())
            {
                Stop(uid, comp);
                changed = true;
            }
        }

        if (!any || !changed)
            return;

        var text = args.Pointed == ent.Owner
            ? "cf-companion-follow"
            : attack
                ? "cf-companion-attack"
                : IsFetchable(args.Pointed)
                    ? "cf-companion-fetch"
                    : "cf-companion-stop";
        _popup.PopupEntity(Loc.GetString(text), ent.Owner, ent.Owner);
    }

    /// <summary>Spawns an ally beside <paramref name="summoner"/> and gives it that creature's company.</summary>
    public EntityUid Summon(EntityUid summoner, string prototype)
    {
        var uid = Spawn(prototype, Transform(summoner).Coordinates);
        Bind(summoner, uid);
        return uid;
    }

    /// <summary>The companion walks with its summoner until it is given another order.</summary>
    public void ComeAlong(EntityUid companion)
    {
        if (TryComp(companion, out CFCompanionComponent? comp))
            Follow(companion, comp);
    }

    /// <summary>
    /// Brings the companions of <paramref name="owner"/> up to its level, from their <c>cfPet</c> tables. Wounds stay as they are:
    /// a companion gains the new Hit Points on top of what it has left, and one that has fallen gains none until it is back up.
    /// </summary>
    public void MatchOwnerLevel(EntityUid owner, CFCombatantComponent ownerCombat)
    {
        var query = EntityQueryEnumerator<CFCompanionComponent, CFCombatantComponent>();
        while (query.MoveNext(out var uid, out var companion, out var combat))
        {
            if (companion.Summoner != owner || !_prototypes.TryIndex<CFPetPrototype>(companion.PetId, out var pet))
                continue;

            var build = new CFCharacterBuildData { Level = ownerCombat.Level, PetBarding = companion.Barding };
            var block = CFPetMath.Present(pet, build, null);
            var gained = block.HitPoints - combat.MaxHp;

            combat.Level = block.Level;
            combat.MaxHp = block.HitPoints;
            combat.ArmorClass = pet.UsesMaster ? ownerCombat.ArmorClass + CFPetMath.BardingOf(companion.Barding).Bonus : block.ArmorClass;
            if (combat.CurrentHp > 0 && gained > 0)
                combat.CurrentHp += gained;

            Dirty(uid, combat);
        }
    }

    public void Bind(EntityUid summoner, EntityUid companion)
    {
        if (!TryComp(companion, out CFCompanionComponent? comp))
            comp = EnsureComp<CFCompanionComponent>(companion);

        comp.Summoner = summoner;
        Dirty(companion, comp);
        EnsureComp<CFSummonerComponent>(summoner);

        if (TryComp(summoner, out CFFactionComponent? faction))
            _factions.SetFaction(companion, faction.Faction);
    }

    private void Order(EntityUid companion, CFCompanionComponent comp, EntityUid target)
    {
        comp.Following = false;
        comp.Fetch = EntityUid.Invalid;
        comp.FetchWaits = 0;
        comp.Target = target;
        Dirty(companion, comp);

        var melee = EnsureComp<NPCMeleeCombatComponent>(companion);
        melee.Target = target;
        melee.MissChance = 0f;
        if (TryComp(companion, out CombatModeComponent? mode))
            _combat.SetInCombatMode(companion, true, mode);
    }

    private void Follow(EntityUid companion, CFCompanionComponent comp)
    {
        // A thrall stands where it is until it is commanded.
        if (HasComp<CFThrallComponent>(companion))
        {
            Stop(companion, comp);
            return;
        }

        comp.Following = true;
        comp.Target = EntityUid.Invalid;
        comp.Fetch = EntityUid.Invalid;
        comp.FetchWaits = 0;
        Dirty(companion, comp);
        RemComp<NPCMeleeCombatComponent>(companion);

        if (HasMind(companion) || !comp.Summoner.IsValid())
            return;

        var steering = _steering.Register(companion, new EntityCoordinates(comp.Summoner, Vector2.Zero));
        steering.Range = FollowRange;
    }

    private void Fetch(EntityUid companion, CFCompanionComponent comp, EntityUid item)
    {
        comp.Following = false;
        comp.Target = EntityUid.Invalid;
        comp.Fetch = item;
        comp.FetchWaits = 0;
        Dirty(companion, comp);
        RemComp<NPCMeleeCombatComponent>(companion);
        if (TryComp(companion, out CombatModeComponent? mode))
            _combat.SetInCombatMode(companion, false, mode);

        if (HasMind(companion))
            return;

        MakeHandFree(companion, item);
        var steering = _steering.Register(companion, new EntityCoordinates(item, Vector2.Zero));
        steering.Range = PickupRange;
        steering.Status = SteeringStatus.Moving;
    }

    private void UpdateFetch(EntityUid uid, CFCompanionComponent comp)
    {
        var item = comp.Fetch;
        if (!Exists(item) || !HasComp<ItemComponent>(item))
        {
            Follow(uid, comp);
            return;
        }

        if (HasMind(uid))
            return;

        if (_hands.IsHolding(uid, item))
        {
            ReturnFetch(uid, comp, item);
            return;
        }

        if (_containers.IsEntityInContainer(item) || !comp.Summoner.IsValid() || !Exists(comp.Summoner))
        {
            Follow(uid, comp);
            return;
        }

        if (TryComp(uid, out CombatModeComponent? mode) && mode.IsInCombatMode)
            _combat.SetInCombatMode(uid, false, mode);

        MakeHandFree(uid, item);

        if (TryComp(uid, out NPCSteeringComponent? stuck)
            && stuck.Status == SteeringStatus.NoPath
            && stuck.FailedPathCount >= NPCSteeringComponent.FailedPathLimit)
        {
            Follow(uid, comp);
            return;
        }

        var here = Transform(uid).Coordinates;
        if (!here.TryDistance(EntityManager, Transform(item).Coordinates, out var distance))
        {
            Follow(uid, comp);
            return;
        }

        if (distance <= SharedInteractionSystem.InteractionRange
            && TryComp(item, out ItemComponent? itemComp)
            && _hands.TryPickupAnyHand(uid, item, checkActionBlocker: true, item: itemComp))
        {
            comp.FetchWaits = 0;
            _steering.Unregister(uid);
            return;
        }

        var steering = _steering.Register(uid, new EntityCoordinates(item, Vector2.Zero));
        steering.Range = PickupRange;

        if (distance > PickupRange)
        {
            comp.FetchWaits = 0;
            return;
        }

        comp.FetchWaits++;
        if (comp.FetchWaits >= FetchGiveUp)
            Follow(uid, comp);
    }

    private void ReturnFetch(EntityUid uid, CFCompanionComponent comp, EntityUid item)
    {
        if (!comp.Summoner.IsValid() || !Exists(comp.Summoner))
        {
            DropForSummoner(uid, comp, item, comp.Summoner);
            return;
        }

        var here = Transform(uid).Coordinates;
        if (!here.TryDistance(EntityManager, Transform(comp.Summoner).Coordinates, out var distance))
        {
            DropForSummoner(uid, comp, item, EntityUid.Invalid);
            return;
        }

        if (distance > FollowRange)
        {
            var steering = _steering.Register(uid, new EntityCoordinates(comp.Summoner, Vector2.Zero));
            steering.Range = FollowRange;
            return;
        }

        _steering.Unregister(uid);
        DropForSummoner(uid, comp, item, comp.Summoner);
    }

    private void DropForSummoner(EntityUid companion, CFCompanionComponent comp, EntityUid item, EntityUid summoner)
    {
        _leaveOnGround.Add(item);
        if (!_hands.TryDrop(companion, item))
        {
            _leaveOnGround.Remove(item);
            return;
        }

        if (summoner.IsValid() && Exists(summoner))
        {
            _transform.PlaceNextTo(item, summoner);
            _popup.PopupEntity(Loc.GetString("cf-companion-fetched"), summoner, summoner);
        }

        Follow(companion, comp);
    }

    private void MakeHandFree(EntityUid companion, EntityUid except)
    {
        if (_hands.TryGetEmptyHand(companion, out _))
            return;

        if (!_hands.TryGetActiveItem(companion, out var held) || held == except)
            return;

        _hands.TryDrop(companion, held.Value);
    }

    private bool IsFetchable(EntityUid uid)
    {
        if (!HasComp<ItemComponent>(uid) || HasComp<MobStateComponent>(uid))
            return false;

        var xform = Transform(uid);
        return !xform.Anchored && !_containers.IsEntityInContainer(uid);
    }

    private void Stop(EntityUid companion, CFCompanionComponent comp)
    {
        comp.Following = false;
        comp.Target = EntityUid.Invalid;
        comp.Fetch = EntityUid.Invalid;
        comp.FetchWaits = 0;
        Dirty(companion, comp);
        RemComp<NPCMeleeCombatComponent>(companion);
        _steering.Unregister(companion);
    }

    private bool IsOrder(EntityUid summoner, EntityUid pointed)
    {
        if (pointed == summoner || !IsLiving(pointed))
            return false;

        return _factions.CanHarm(summoner, pointed);
    }

    private bool IsLiving(EntityUid uid)
    {
        return TryComp(uid, out MobStateComponent? state) && state.CurrentState != MobState.Dead;
    }

    private bool HasMind(EntityUid uid)
    {
        return TryComp(uid, out MindContainerComponent? mind) && mind.HasMind;
    }

    private EntityUid? FindSummoner(EntityUid companion)
    {
        var origin = Transform(companion).Coordinates;
        EntityUid? best = null;
        var bestDistance = float.MaxValue;

        foreach (var other in _lookup.GetEntitiesInRange<MindContainerComponent>(origin, BindRange))
        {
            if (other.Owner == companion || HasComp<CFCompanionComponent>(other.Owner) || !other.Comp.HasMind)
                continue;

            if (!origin.TryDistance(EntityManager, Transform(other.Owner).Coordinates, out var distance))
                continue;

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = other.Owner;
        }

        return best;
    }
}
