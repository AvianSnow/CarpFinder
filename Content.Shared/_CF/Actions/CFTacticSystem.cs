using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.Network;

namespace Content.Shared.CF.Actions;

/// <summary>A Commander's banner. Carrying one is what makes tactics and the banner's aura work.</summary>
[RegisterComponent]
public sealed partial class CFCommanderBannerComponent : Component
{
}

/// <summary>
/// A tactic: an action a Commander signals to their squad. Parent an action on <c>CFActivityInstant</c>, or on
/// <c>CFActivityTarget</c> to signal one creature, and grant it with <c>CFGrantAction</c>. <see cref="CFTacticSystem"/> runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFTacticActionComponent : Component
{
    /// <summary>Runs on each member of the squad.</summary>
    [DataField]
    public List<CFEffect> SquadEffects = new();

    /// <summary>Each member raises a held shield.</summary>
    [DataField]
    public bool RaisesShields;

    /// <summary>Each member loads the weapon they hold.</summary>
    [DataField]
    public bool Reloads;

    /// <summary>Each member loses 1 from frightened.</summary>
    [DataField]
    public bool EasesFrightened;

    /// <summary>Enemies next to any member are off-guard to the whole squad for 6 seconds.</summary>
    [DataField]
    public bool OffGuardAdjacent;

    /// <summary>The target is a squad member, who Strikes the nearest enemy in reach.</summary>
    [DataField]
    public bool TargetStrikes;

    /// <summary>The target is an enemy with at least this many of the squad in reach. Then the action's check runs on it.</summary>
    [DataField]
    public int NeedsSquadAdjacent;
}

/// <summary>
/// Commander's tactics. The squad is the Commander and their 2 + Intelligence nearest allies inside the banner's 30 foot aura. Real
/// time cannot make other players move, so what the rules call a Stride or a Step is a burst of Speed, and responding to a tactic
/// never spends anyone's reaction.
/// </summary>
public sealed partial class CFTacticSystem : EntitySystem
{
    private const int BannerFeet = 30;
    private const float OffGuardSeconds = 6f;

    /// <summary>A bag in a bag is as deep as a banner is looked for.</summary>
    private const int MaxDepth = 2;

    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFReloadSystem _reload = default!;
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;

    public bool HasBanner(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (IsOrHoldsBanner(held, 0))
                return true;
        }

        var worn = _inventory.GetSlotEnumerator(user);
        while (worn.MoveNext(out var container))
        {
            if (container.ContainedEntity is { } item && IsOrHoldsBanner(item, 0))
                return true;
        }

        return false;
    }

    private bool IsOrHoldsBanner(EntityUid item, int depth)
    {
        if (HasComp<CFCommanderBannerComponent>(item))
            return true;

        if (depth >= MaxDepth || !_containers.TryGetContainer(item, StorageComponent.ContainerId, out var storage))
            return false;

        foreach (var inside in storage.ContainedEntities)
        {
            if (IsOrHoldsBanner(inside, depth + 1))
                return true;
        }

        return false;
    }

    /// <summary>The Commander, then their nearest allies inside the aura, up to 2 + Intelligence of them.</summary>
    public List<EntityUid> Squad(EntityUid user)
    {
        var origin = _transform.GetMapCoordinates(user);
        var allies = _effects.CollectArea(
            user,
            CFAreaShape.Emanation,
            BannerFeet,
            origin,
            origin,
            atPoint: false,
            includeCenter: true,
            selfAllowed: false,
            CFTargetFilter.Allies);

        var position = origin.Position;
        allies.Sort((a, b) => (_transform.GetWorldPosition(a) - position).LengthSquared()
            .CompareTo((_transform.GetWorldPosition(b) - position).LengthSquared()));

        var size = 2 + Math.Max(0, CompOrNull<CFAbilityScoreComponent>(user)?.GetModifier(CFAbility.Intelligence) ?? 0);
        if (allies.Count > size)
            allies.RemoveRange(size, allies.Count - size);

        allies.Insert(0, user);
        return allies;
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFTacticActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (HasBanner(args.User))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-tactic-needs-banner");
    }

    [SubscribeLocalEvent]
    private void OnInstant(Entity<CFTacticActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_net.IsServer)
            Signal(ent, args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnTarget(Entity<CFTacticActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        // Not handled means the action costs nothing, which is right for one that could not be used.
        if (WhyNot(ent, args.Performer, args.Target) is { } reason)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString(reason), args.Performer, args.Performer);

            return;
        }

        args.Handled = true;
        if (!_net.IsServer)
            return;

        if (ent.Comp.TargetStrikes)
        {
            if (NearestEnemy(args.Target) is { } enemy)
                _strikes.ReactionStrike(args.Target, enemy);

            return;
        }

        _effects.ResolveTarget(ent, args.Performer, args.Target);
    }

    private string? WhyNot(Entity<CFTacticActionComponent> ent, EntityUid user, EntityUid target)
    {
        if (ent.Comp.TargetStrikes)
        {
            if (!Squad(user).Contains(target))
                return "cf-tactic-needs-squadmate";

            return NearestEnemy(target) == null ? "cf-tactic-no-enemy" : null;
        }

        if (ent.Comp.NeedsSquadAdjacent <= 0)
            return null;

        if (!_factions.CanHarm(user, target))
            return "cf-act-cannot";

        var around = 0;
        foreach (var member in Squad(user))
        {
            if (InReach(member, target))
                around++;
        }

        return around >= ent.Comp.NeedsSquadAdjacent ? null : "cf-tactic-needs-flankers";
    }

    private void Signal(Entity<CFTacticActionComponent> ent, EntityUid user)
    {
        var squad = Squad(user);
        foreach (var member in squad)
        {
            _effects.Run(ent.Comp.SquadEffects, new CFEffectArgs
            {
                User = user,
                Source = ent,
                Target = member,
                Position = Transform(member).Coordinates,
            });

            if (ent.Comp.RaisesShields && _shields.TryGetUsableShield(member, out var shield))
                _shields.SetRaised(shield, true);

            if (ent.Comp.Reloads)
                _reload.TryReload(member, prepaid: false);

            if (ent.Comp.EasesFrightened)
                _conditions.ReduceFrightened(member, 1);
        }

        if (ent.Comp.OffGuardAdjacent)
            MarkOffGuard(squad);

        _popup.PopupEntity(Loc.GetString("cf-tactic-signal", ("name", MetaData(ent).EntityName)), user);
    }

    private void MarkOffGuard(List<EntityUid> squad)
    {
        foreach (var member in squad)
        {
            foreach (var (enemy, _) in _lookup.GetEntitiesInRange<CFCombatantComponent>(Transform(member).Coordinates, 2f))
            {
                if (!_factions.CanHarm(member, enemy) || !InReach(member, enemy))
                    continue;

                foreach (var other in squad)
                {
                    _conditions.SetOffGuardTo(enemy, other, OffGuardSeconds);
                }
            }
        }
    }

    private EntityUid? NearestEnemy(EntityUid member)
    {
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var position = _transform.GetWorldPosition(member);
        foreach (var (enemy, _) in _lookup.GetEntitiesInRange<CFCombatantComponent>(Transform(member).Coordinates, 2f))
        {
            if (!_factions.CanHarm(member, enemy) || !InReach(member, enemy))
                continue;

            var distance = (_transform.GetWorldPosition(enemy) - position).LengthSquared();
            if (distance < bestDistance)
            {
                best = enemy;
                bestDistance = distance;
            }
        }

        return best;
    }

    private bool InReach(EntityUid member, EntityUid enemy)
    {
        return CFFlanking.InReach(_transform.GetWorldPosition(member), _transform.GetWorldPosition(enemy), CFFlanking.DefaultReachFeet);
    }
}
