using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.Factions;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Resolves a source (spell action, bomb, trap, aura) against its targets:
/// range and filter, area, the check, then the effect lists for the degree of success.
/// </summary>
/// <remarks>
/// Callers decide when a source fires and what it costs (spell slots, actions, charges).
/// This system then does the same thing for every caller. It keeps no state of its own:
/// what lasts is on components (<see cref="CFHomingProjectileComponent"/>, <see cref="CFBonusesComponent"/>).
/// Resolution runs on the server only.
/// </remarks>
public sealed partial class CFEffectSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFCombatBridgeSystem _bridge = default!;
    [Dependency] private CFDyingSystem _dying = default!;
    [Dependency] private CFShieldSystem _shields = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        SteerBolts();
    }

    #region Targets

    /// <summary>
    /// Dead creatures are never affected. The user counts only when <paramref name="selfAllowed"/>
    /// and the filter is not <see cref="CFTargetFilter.Enemies"/>.
    /// </summary>
    public bool PassesFilter(EntityUid user, EntityUid target, CFTargetFilter filter, bool selfAllowed)
    {
        if (TryComp(target, out MobStateComponent? mob) && _mobState.IsDead(target, mob))
            return false;

        if (target == user)
            return selfAllowed && filter != CFTargetFilter.Enemies;

        return filter switch
        {
            CFTargetFilter.Enemies => _factions.CanHarm(user, target),
            CFTargetFilter.Allies => !_factions.CanHarm(user, target),
            _ => true,
        };
    }

    /// <summary>The clicked creature is a legal target for this source.</summary>
    public bool CanTarget(EntityUid source, EntityUid user, EntityUid target)
    {
        var filter = CompOrNull<CFTargetingComponent>(source)?.Filter ?? CFTargetFilter.Enemies;
        return PassesFilter(user, target, filter, selfAllowed: true);
    }

    public bool InRange(EntityUid source, EntityUid user, EntityUid target)
    {
        if (!TryComp(source, out CFTargetingComponent? targeting))
            return true;

        var from = _transform.GetMapCoordinates(user);
        var to = _transform.GetMapCoordinates(target);
        if (from.MapId != to.MapId)
            return false;

        var feet = (from.Position - to.Position).Length() * CFAreaGrid.FeetPerTile;
        if (targeting.RangeFeet > 0 && feet > targeting.RangeFeet + 0.05f)
            return false;

        return !targeting.LineOfEffect
               || user == target
               || _interaction.InRangeUnobstructed(user, target, feet / CFAreaGrid.FeetPerTile + 1f);
    }

    public bool InRange(EntityUid source, EntityUid user, MapCoordinates point)
    {
        if (!TryComp(source, out CFTargetingComponent? targeting))
            return true;

        var from = _transform.GetMapCoordinates(user);
        if (from.MapId != point.MapId)
            return false;

        var feet = (from.Position - point.Position).Length() * CFAreaGrid.FeetPerTile;
        if (targeting.RangeFeet > 0 && feet > targeting.RangeFeet + 0.05f)
            return false;

        return !targeting.LineOfEffect
               || _interaction.InRangeUnobstructed(from, point, feet / CFAreaGrid.FeetPerTile + 1f);
    }

    /// <summary>
    /// Sends the source at a clicked creature: bolts for projectile delivery, otherwise it resolves at once
    /// and then on any <see cref="CFTargetingComponent.ExtraTargets"/>.
    /// </summary>
    public void DeliverToTarget(EntityUid source, EntityUid user, EntityUid target)
    {
        if (TryComp(source, out CFTargetingComponent? targeting)
            && targeting.Delivery == CFDelivery.Projectile
            && TryComp(source, out CFProjectileDeliveryComponent? bolts))
        {
            LaunchBolts(source, user, target, bolts.IsVolley ? bolts.ClampedBolts : 1);
            return;
        }

        ResolveTarget(source, user, target);
        if (targeting is { ExtraTargets: > 0 })
            ChainTargets(source, user, target, targeting);
    }

    private void ChainTargets(EntityUid source, EntityUid user, EntityUid first, CFTargetingComponent targeting)
    {
        var origin = _transform.GetMapCoordinates(user);
        var reach = Math.Max(1f, targeting.RangeFeet / CFAreaGrid.FeetPerTile);
        var left = targeting.ExtraTargets;
        foreach (var uid in _lookup.GetEntitiesInRange(origin, reach))
        {
            if (uid == user || uid == first || !HasComp<MobStateComponent>(uid))
                continue;

            if (!PassesFilter(user, uid, targeting.Filter, selfAllowed: false) || !InRange(source, user, uid))
                continue;

            ResolveTarget(source, user, uid);
            if (--left <= 0)
                return;
        }
    }

    #endregion

    #region Areas

    /// <summary>The user stands on a grid, the point is on the same map, and a burst is within range.</summary>
    public bool ValidateArea(EntityUid source, EntityUid user, MapCoordinates aim, out string? failure)
    {
        failure = null;
        if (!TryComp(source, out CFAreaEffectComponent? area))
            return true;

        var userMap = _transform.GetMapCoordinates(user);
        if (!_maps.TryFindGridAt(userMap, out _, out _))
        {
            failure = "cf-spell-no-grid";
            return false;
        }

        if (userMap.MapId != aim.MapId)
        {
            failure = "cf-spell-out-of-range";
            return false;
        }

        if (area.StartsAtPoint() && !InRange(source, user, aim))
        {
            failure = "cf-spell-out-of-range";
            return false;
        }

        return true;
    }

    /// <summary>Resolves the source on every creature in its area. Returns how many were affected.</summary>
    public int ResolveArea(EntityUid source, EntityUid user, MapCoordinates aim)
    {
        if (!TryComp(source, out CFAreaEffectComponent? area))
            return 0;

        var origin = _transform.GetMapCoordinates(user);
        var targets = CollectArea(
            user,
            area.Shape,
            area.SizeFeet,
            origin,
            aim,
            area.StartsAtPoint(),
            area.IncludeSelf,
            area.IncludeSelf,
            area.Filter,
            area.Whitelist,
            area.MaxTargets);

        foreach (var target in targets)
        {
            ResolveTarget(source, user, target);
        }

        if (targets.Count == 0)
            _popup.PopupEntity(Loc.GetString("cf-spell-no-one"), user, user);

        return targets.Count;
    }

    /// <summary>
    /// Creatures standing in an area's tiles that pass the filter.
    /// </summary>
    /// <param name="origin">Where cones, lines, and emanations start.</param>
    /// <param name="aim">Where bursts and squares are centered, and the direction for cones and lines.</param>
    /// <param name="atPoint">The area is centered on <paramref name="aim"/>, so the lookup searches there.</param>
    /// <param name="includeCenter">An emanation covers its own center square.</param>
    /// <param name="selfAllowed">The user may be among the targets.</param>
    public List<EntityUid> CollectArea(
        EntityUid user,
        CFAreaShape shape,
        int sizeFeet,
        MapCoordinates origin,
        MapCoordinates aim,
        bool atPoint,
        bool includeCenter,
        bool selfAllowed,
        CFTargetFilter filter,
        EntityWhitelist? whitelist = null,
        int maxTargets = 0)
    {
        var found = new List<EntityUid>();
        if (origin.MapId != aim.MapId || !_maps.TryFindGridAt(origin, out var grid, out var gridComp))
            return found;

        var originLocal = _maps.WorldToLocal(grid, gridComp, origin.Position);
        var aimLocal = _maps.WorldToLocal(grid, gridComp, aim.Position);
        var tiles = CFAreaGrid.Collect(shape, sizeFeet, originLocal, aimLocal, gridComp.TileSize, includeCenter);

        var searchFrom = atPoint ? aim : origin;
        var reach = Math.Max(3f, sizeFeet / CFAreaGrid.FeetPerTile + 3f);
        foreach (var uid in _lookup.GetEntitiesInRange(searchFrom, reach))
        {
            if (!HasComp<MobStateComponent>(uid) || !TryComp(uid, out TransformComponent? xform) || xform.GridUid != grid)
                continue;

            var local = _maps.WorldToLocal(grid, gridComp, _transform.GetWorldPosition(uid));
            if (!tiles.Contains(CFAreaGrid.ToTile(local, gridComp.TileSize)))
                continue;

            if (!PassesFilter(user, uid, filter, selfAllowed))
                continue;

            if (whitelist != null && !_whitelist.IsValid(whitelist, uid))
                continue;

            found.Add(uid);
            if (maxTargets > 0 && found.Count >= maxTargets)
                break;
        }

        return found;
    }

    #endregion

    #region Checks

    /// <summary>
    /// Rolls the source's check against one creature and runs its effect lists.
    /// Concealment and blindness force a flat check first. Returns true when the check landed.
    /// </summary>
    public bool ResolveTarget(EntityUid source, EntityUid user, EntityUid target)
    {
        if (user != target && !_conditions.PassFlatCheck(user, target))
            return false;

        var check = CompOrNull<CFCheckComponent>(source);
        var args = BuildArgs(source, user, target, Transform(target).Coordinates, check, out var stats);

        var landed = true;
        if (check != null && check.Defense == CFDefenseType.ArmorClass)
        {
            args.Degree = RollAttack(user, target, check, stats.AttackBonus);
            landed = args.Degree.IsSuccess();
        }
        else if (check != null && check.Defense != CFDefenseType.None)
        {
            args.Degree = RollDefense(target, check.Defense, stats.Dc);
            landed = !args.Degree.IsSuccess();
        }

        RunDegreeEffects(source, args, landed);
        return landed;
    }

    /// <summary>The effect context for a user and source, without rolling anything.</summary>
    public CFEffectArgs BuildArgs(
        EntityUid source,
        EntityUid user,
        EntityUid? target,
        EntityCoordinates? position,
        CFCheckComponent? check,
        out CFCastingStats stats)
    {
        var combat = CompOrNull<CFCombatantComponent>(user);
        var abilities = CompOrNull<CFAbilityScoreComponent>(user);
        stats = check?.DcSource switch
        {
            CFDcSource.Class => CFCasting.ClassDc(_prototypes, combat, abilities),
            CFDcSource.Fixed => CFCasting.Fixed(check.FixedDc),
            _ => CFCasting.Spellcasting(_prototypes, combat, abilities),
        };

        var context = new CFEffectContextEvent(user);
        RaiseLocalEvent(source, ref context);

        var steps = TryComp(source, out CFHeightenComponent? heighten) ? heighten.Steps(context.Rank) : 0;
        return new CFEffectArgs
        {
            User = user,
            Source = source,
            Target = target,
            Position = position,
            Defense = check?.Defense ?? CFDefenseType.None,
            BasicSave = check?.BasicSave ?? false,
            Rank = context.Rank,
            HeightenSteps = steps,
            Dc = stats.Dc,
            AbilityModifier = stats.AbilityModifier,
        };
    }

    private void RunDegreeEffects(EntityUid source, CFEffectArgs args, bool landed)
    {
        if (!TryComp(source, out CFDegreeEffectsComponent? lists))
            return;

        Run(lists.Effects, args);
        if (landed)
        {
            Run(lists.Landed, args);
            if (TryComp(source, out CFHeightenComponent? heighten))
            {
                foreach (var (rank, extra) in heighten.AtRank)
                {
                    if (args.Rank >= rank)
                        Run(extra, args);
                }
            }
        }

        Run(args.Degree switch
            {
                CFDegree.CriticalSuccess => lists.CriticalSuccess,
                CFDegree.Success => lists.Success,
                CFDegree.Failure => lists.Failure,
                _ => lists.CriticalFailure,
            },
            args);

        // A save against a pure rider (fear, sleep) that did nothing gets a word, so the cast does not look lost.
        if (!landed
            && args.Defense is not (CFDefenseType.None or CFDefenseType.ArmorClass)
            && lists.Effects.Count == 0
            && args.Target is { } target)
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-resisted"), target);
        }
    }

    public void Run(IReadOnlyList<CFEffect> effects, CFEffectArgs args)
    {
        foreach (var effect in effects)
        {
            effect.Apply(EntityManager, args);
        }
    }

    /// <summary>
    /// The user's attack roll against the target's AC. Takes and adds to the multiple attack penalty.
    /// Bonuses, frightened, and sure strike apply.
    /// </summary>
    private CFDegree RollAttack(EntityUid user, EntityUid target, CFCheckComponent check, int attackBonus)
    {
        var armorClass = CFCommonerStats.ArmorClass;
        if (_bridge.TryEnsureCombatTarget(target, out var defender, out _, out _))
            armorClass = defender.ArmorClass;

        armorClass += _bonuses.Total(target, CFBonusStat.ArmorClass);
        armorClass += _shields.RaisedBonus(target);
        armorClass -= _conditions.Frightened(target);
        armorClass -= _conditions.Clumsy(target);
        armorClass -= check.TargetAcPenalty;

        attackBonus += check.AttackModifier;
        attackBonus += _bonuses.Total(user, CFBonusStat.AttackRoll);
        attackBonus -= _conditions.Frightened(user);
        if (check.MultipleAttackPenalty)
        {
            attackBonus += _actions.MapPenalty(user, agile: false);
            _actions.CountAttack(user);
        }

        var d20 = _random.Next(1, 21);
        if (_bonuses.TryConsumeFortune(user))
            d20 = Math.Max(d20, _random.Next(1, 21));

        return CFDegrees.DegreeOfSuccess(d20, attackBonus, armorClass);
    }

    /// <summary>The target's save or Perception check against the DC.</summary>
    public CFDegree RollDefense(EntityUid target, CFDefenseType defense, int dc)
    {
        var combat = CompOrNull<CFCombatantComponent>(target);
        var abilities = CompOrNull<CFAbilityScoreComponent>(target);
        var frightened = _conditions.Frightened(target);
        int modifier;
        if (defense == CFDefenseType.Perception)
        {
            modifier = CFDefense.PerceptionModifier(
                _prototypes,
                combat,
                abilities,
                _bonuses.Total(target, CFBonusStat.Perception),
                frightened);
        }
        else
        {
            var status = _bonuses.Total(target, CFBonusStat.Saves);
            if (defense == CFDefenseType.Reflex)
                status -= _conditions.Clumsy(target);

            modifier = CFDefense.SaveModifier(_prototypes, combat, abilities, defense, status, frightened);
        }

        return CFDefense.Roll(_random, modifier, dc);
    }

    #endregion
}

/// <summary>
/// Raised on a source before it resolves, so the owning system can say what rank it resolves at.
/// Spells fill this from <c>CFSpellComponent</c>. Other sources leave it at 0 (no heightening).
/// </summary>
[ByRefEvent]
public record struct CFEffectContextEvent(EntityUid User)
{
    public int Rank;
}
