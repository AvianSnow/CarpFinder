using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

/// <summary>A Commander's banner. Carrying one is what makes tactics and the banner's aura work.</summary>
[RegisterComponent]
public sealed partial class CFCommanderBannerComponent : Component
{
}

/// <summary>A banner set in the ground by Plant Banner. Allies around it keep a few temporary Hit Points while it stands.</summary>
[RegisterComponent]
public sealed partial class CFPlantedBannerComponent : Component
{
    [DataField]
    public EntityUid Commander;

    [DataField]
    public TimeSpan NextPulse;
}

/// <summary>The Plant Banner action: plants the held banner, or takes a planted one back.</summary>
[RegisterComponent]
public sealed partial class CFPlantBannerActionComponent : Component
{
}

/// <summary>An action that needs the Commander's banner, such as Banner Twirl, but is not a tactic.</summary>
[RegisterComponent]
public sealed partial class CFNeedsBannerComponent : Component
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

    /// <summary>Runs on the squad member the tactic is aimed at (Protective Screen).</summary>
    [DataField]
    public List<CFEffect> TargetEffects = new();

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

    /// <summary>
    /// Double Team: the target is an enemy with a squad member in reach. That member Shoves it, and if it then stands in
    /// another member's reach, that one Strikes it.
    /// </summary>
    [DataField]
    public bool DoubleTeam;

    /// <summary>
    /// Coordinating Maneuvers: the target is an enemy with a squad member in reach, who Repositions it. A success moves it
    /// 5 feet away from the Commander, a critical success 10, so the squad can get round it.
    /// </summary>
    [DataField]
    public bool Repositions;

    /// <summary>Passage of Lines: squad members standing beside each other trade places.</summary>
    [DataField]
    public bool SwapsPositions;

    /// <summary>
    /// End It!: only when the squad outnumbers the enemies about and one of them dropped an enemy in the last 6 seconds.
    /// Every enemy within 10 feet of a squad member saves against the action's check.
    /// </summary>
    [DataField]
    public bool EndIt;
}

/// <summary>
/// Commander's tactics. The squad is the Commander and their 2 + Intelligence nearest allies inside the banner's 30 foot aura,
/// and always their own companion. Real time cannot make other players move, so what the rules call a Stride or a Step is a
/// burst of Speed, and responding to a tactic never spends anyone's reaction. A planted banner stands for the Commander's,
/// wherever they are.
/// </summary>
public sealed partial class CFTacticSystem : EntitySystem
{
    private const int BannerFeet = 30;
    private const float OffGuardSeconds = 6f;
    private const float EndItFeet = 10f;
    private const float OutnumberFeet = 60f;
    private static readonly TimeSpan RecentDrop = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan BannerPulse = TimeSpan.FromSeconds(6);

    /// <summary>A bag in a bag is as deep as a banner is looked for.</summary>
    private const int MaxDepth = 2;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFReloadSystem _reload = default!;
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private CFStrikeSystem _strikes = default!;
    [Dependency] private CFTemporaryHpSystem _tempHp = default!;

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

        return PlantedBanner(user) != null;
    }

    /// <summary>The banner this Commander has planted, if it still stands.</summary>
    public EntityUid? PlantedBanner(EntityUid user)
    {
        var query = EntityQueryEnumerator<CFPlantedBannerComponent>();
        while (query.MoveNext(out var uid, out var planted))
        {
            if (planted.Commander == user)
                return uid;
        }

        return null;
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

    /// <summary>The Commander, then their nearest allies inside the aura, up to 2 + Intelligence of them, and always their companion.</summary>
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

        // Commander's Companion: it is always in the squad, and takes no place in it.
        var companions = new List<EntityUid>();
        foreach (var ally in allies)
        {
            if (TryComp(ally, out CFCompanionComponent? companion) && companion.Summoner == user)
                companions.Add(ally);
        }

        allies.RemoveAll(companions.Contains);
        var size = 2 + Math.Max(0, CompOrNull<CFAbilityScoreComponent>(user)?.GetModifier(CFAbility.Intelligence) ?? 0);
        if (allies.Count > size)
            allies.RemoveRange(size, allies.Count - size);

        allies.AddRange(companions);
        allies.Insert(0, user);
        return allies;
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFTacticActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (!HasBanner(args.User))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-tactic-needs-banner");
            return;
        }

        if (ent.Comp.EndIt && WhyNotEndIt(args.User) is { } reason)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString(reason);
        }
    }

    [SubscribeLocalEvent]
    private void OnNeedsBannerAttempt(Entity<CFNeedsBannerComponent> ent, ref ActionAttemptEvent args)
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

        var user = args.Performer;
        var target = args.Target;
        if (ent.Comp.TargetStrikes)
        {
            if (NearestEnemy(target) is { } enemy)
                _strikes.ReactionStrike(target, enemy);

            return;
        }

        if (ent.Comp.TargetEffects.Count > 0)
        {
            _effects.Run(ent.Comp.TargetEffects, new CFEffectArgs { User = user, Source = ent, Target = target, Position = Transform(target).Coordinates });
            _popup.PopupEntity(Loc.GetString("cf-tactic-signal", ("name", MetaData(ent).EntityName)), user);
            return;
        }

        if (ent.Comp.DoubleTeam)
        {
            DoubleTeam(user, target);
            return;
        }

        if (ent.Comp.Repositions)
        {
            Reposition(user, target);
            return;
        }

        _effects.ResolveTarget(ent, user, target);
    }

    private string? WhyNot(Entity<CFTacticActionComponent> ent, EntityUid user, EntityUid target)
    {
        if (ent.Comp.TargetStrikes)
        {
            if (!Squad(user).Contains(target))
                return "cf-tactic-needs-squadmate";

            return NearestEnemy(target) == null ? "cf-tactic-no-enemy" : null;
        }

        if (ent.Comp.TargetEffects.Count > 0)
            return Squad(user).Contains(target) ? null : "cf-tactic-needs-squadmate";

        if (ent.Comp.DoubleTeam || ent.Comp.Repositions)
        {
            if (!_factions.CanHarm(user, target))
                return "cf-act-cannot";

            return MemberInReach(user, target) == null ? "cf-tactic-needs-flankers" : null;
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

        if (ent.Comp.SwapsPositions)
            SwapPairs(squad);

        if (ent.Comp.EndIt)
            EndIt(ent, user, squad);

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

    /// <summary>Passage of Lines: each member trades places with the nearest member beside them that has not moved yet.</summary>
    private void SwapPairs(List<EntityUid> squad)
    {
        var moved = new HashSet<EntityUid>();
        foreach (var member in squad)
        {
            if (moved.Contains(member))
                continue;

            EntityUid? partner = null;
            var best = float.MaxValue;
            var position = _transform.GetWorldPosition(member);
            foreach (var other in squad)
            {
                if (other == member || moved.Contains(other) || !InReach(member, other))
                    continue;

                var distance = (_transform.GetWorldPosition(other) - position).LengthSquared();
                if (distance >= best)
                    continue;

                best = distance;
                partner = other;
            }

            if (partner is not { } pair)
                continue;

            _transform.SwapPositions(member, pair);
            moved.Add(member);
            moved.Add(pair);
        }
    }

    /// <summary>Double Team: the nearest member in reach Shoves the enemy. If it then stands in another member's reach, that one Strikes.</summary>
    private void DoubleTeam(EntityUid user, EntityUid enemy)
    {
        if (MemberInReach(user, enemy) is not { } shover)
            return;

        var degree = _conditions.RollSkill(shover, enemy, "Athletics", CFAbility.Strength, CFDefenseType.Fortitude, attackTrait: true);
        if (!degree.IsSuccess())
        {
            _popup.PopupEntity(Loc.GetString("cf-tactic-shove-fails"), enemy);
            return;
        }

        var feet = degree == CFDegree.CriticalSuccess ? 10 : 5;
        _conditions.Push(shover, enemy, feet);

        // Where it ends up: the push is a shove through the air, so look where it is going.
        var direction = _transform.GetWorldPosition(enemy) - _transform.GetWorldPosition(shover);
        if (direction.LengthSquared() > 0.01f)
            direction = System.Numerics.Vector2.Normalize(direction) * (feet / CFMelee.FeetPerTile);

        var landing = _transform.GetWorldPosition(enemy) + direction;
        foreach (var member in Squad(user))
        {
            if (member == shover || !CFFlanking.InReach(_transform.GetWorldPosition(member), landing, CFFlanking.DefaultReachFeet))
                continue;

            _strikes.ReactionStrike(member, enemy);
            return;
        }
    }

    /// <summary>Coordinating Maneuvers: the member in reach Repositions the enemy, away from the Commander.</summary>
    private void Reposition(EntityUid user, EntityUid enemy)
    {
        if (MemberInReach(user, enemy) is not { } mover)
            return;

        var degree = _conditions.RollSkill(mover, enemy, "Athletics", CFAbility.Strength, CFDefenseType.Fortitude, attackTrait: true);
        if (!degree.IsSuccess())
        {
            _popup.PopupEntity(Loc.GetString("cf-tactic-shove-fails"), enemy);
            return;
        }

        _conditions.Push(user, enemy, degree == CFDegree.CriticalSuccess ? 10 : 5);
    }

    /// <summary>End It!: enemies within 10 feet of a squad member save against the action's check, which frightens them.</summary>
    private void EndIt(Entity<CFTacticActionComponent> ent, EntityUid user, List<EntityUid> squad)
    {
        var done = new HashSet<EntityUid>();
        foreach (var member in squad)
        {
            foreach (var (enemy, _) in _lookup.GetEntitiesInRange<CFCombatantComponent>(Transform(member).Coordinates, EndItFeet / CFMelee.FeetPerTile))
            {
                if (!done.Add(enemy) || !_factions.CanHarm(user, enemy) || !_mobState.IsAlive(enemy))
                    continue;

                _effects.ResolveTarget(ent, user, enemy);
            }
        }
    }

    private string? WhyNotEndIt(EntityUid user)
    {
        var now = _timing.CurTime;
        var recent = false;
        var squad = Squad(user);
        foreach (var member in squad)
        {
            if (TryComp(member, out CFCombatOptionsComponent? options) && options.LastDroppedEnemyAt != TimeSpan.Zero && now - options.LastDroppedEnemyAt <= RecentDrop)
                recent = true;
        }

        if (!recent)
            return "cf-tactic-needs-drop";

        var allies = 0;
        var enemies = 0;
        foreach (var (uid, _) in _lookup.GetEntitiesInRange<CFCombatantComponent>(Transform(user).Coordinates, OutnumberFeet / CFMelee.FeetPerTile))
        {
            if (!_mobState.IsAlive(uid))
                continue;

            if (uid == user || !_factions.CanHarm(user, uid))
                allies++;
            else
                enemies++;
        }

        return allies > enemies ? null : "cf-tactic-outnumbered";
    }

    private EntityUid? MemberInReach(EntityUid user, EntityUid enemy)
    {
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var position = _transform.GetWorldPosition(enemy);
        foreach (var member in Squad(user))
        {
            if (!InReach(member, enemy))
                continue;

            var distance = (_transform.GetWorldPosition(member) - position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = member;
            bestDistance = distance;
        }

        return best;
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

    // Plant Banner

    [SubscribeLocalEvent]
    private void OnPlantAttempt(Entity<CFPlantBannerActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (HeldBanner(args.User) != null || PlantedBannerInReach(args.User) != null)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-tactic-needs-banner-in-hand");
    }

    /// <summary>Plants the held banner at the Commander's feet, or takes a planted one back into a hand.</summary>
    [SubscribeLocalEvent]
    private void OnPlant(Entity<CFPlantBannerActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        var user = args.Performer;
        if (HeldBanner(user) is { } banner)
        {
            _hands.TryDrop(user, banner, checkActionBlocker: false);
            _transform.SetCoordinates(banner, Transform(user).Coordinates);
            var planted = EnsureComp<CFPlantedBannerComponent>(banner);
            planted.Commander = user;
            planted.NextPulse = _timing.CurTime;
            _popup.PopupEntity(Loc.GetString("cf-tactic-banner-planted"), user);
            return;
        }

        if (PlantedBannerInReach(user) is not { } standing)
            return;

        RemComp<CFPlantedBannerComponent>(standing);
        _hands.TryPickupAnyHand(user, standing, animate: false);
        _popup.PopupEntity(Loc.GetString("cf-tactic-banner-taken"), user, user);
    }

    private EntityUid? HeldBanner(EntityUid user)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<CFCommanderBannerComponent>(held))
                return held;
        }

        return null;
    }

    private EntityUid? PlantedBannerInReach(EntityUid user)
    {
        return PlantedBanner(user) is { } banner && InReach(user, banner) ? banner : null;
    }

    /// <summary>What a planted banner gives each ally beside it: 4 temporary Hit Points, and 4 more at level 4 and every 4 after.</summary>
    public static int BannerTemporaryHp(int level)
    {
        return 4 * (1 + level / 4);
    }

    /// <summary>A planted banner renews its allies' temporary Hit Points every round. Someone else picking it up ends that.</summary>
    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFPlantedBannerComponent>();
        while (query.MoveNext(out var uid, out var planted))
        {
            if (_containers.IsEntityInContainer(uid))
            {
                RemCompDeferred(uid, planted);
                continue;
            }

            if (now < planted.NextPulse)
                continue;

            planted.NextPulse = now + BannerPulse;
            if (!Exists(planted.Commander))
                continue;

            var amount = BannerTemporaryHp(CompOrNull<CFCombatantComponent>(planted.Commander)?.Level ?? 1);
            foreach (var (ally, _) in _lookup.GetEntitiesInRange<CFCombatantComponent>(Transform(uid).Coordinates, BannerFeet / CFMelee.FeetPerTile))
            {
                if (ally == planted.Commander || !_factions.CanHarm(planted.Commander, ally))
                    _tempHp.Grant(ally, amount, (float) BannerPulse.TotalSeconds + 0.5f, "banner");
            }
        }
    }
}
