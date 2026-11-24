using Content.Shared.CF.Preferences;
using Content.Shared.Actions.Components;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
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
    [Dependency] private CFSpellshapeSystem _spellshapes = default!;
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
        var targeting = CompOrNull<CFTargetingComponent>(source);
        if (targeting is { TargetTraits.Count: > 0 }
            && CompOrNull<Content.Shared.CF.Traits.CFTraitComponent>(target)?.HasAny(targeting.TargetTraits) != true)
        {
            return false;
        }

        return PassesFilter(user, target, targeting?.Filter ?? CFTargetFilter.Enemies, selfAllowed: true);
    }

    /// <summary>How far a source reaches, in feet. No limit when it has no range.</summary>
    private float MaxFeet(EntityUid source, EntityUid user, CFTargetingComponent targeting)
    {
        var extra = _spellshapes.ExtraRangeFeet(source, user);

        // The chosen mode can set its own range (Heal's 1-action touch).
        if (TryComp(source, out CFModeChoiceComponent? mode) && mode.ChosenOption() is { } chosen)
        {
            if (chosen.Touch)
                return extra > 0 ? extra : TouchFeet;

            if (chosen.RangeFeet > 0)
                return chosen.RangeFeet + extra;
        }

        if (targeting.Touch)
            return extra > 0 ? extra : TouchFeet;

        return targeting.RangeFeet > 0 ? targeting.RangeFeet + extra : float.MaxValue;
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
        if (feet > MaxFeet(source, user, targeting) + 0.05f)
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
        if (feet > MaxFeet(source, user, targeting) + 0.05f)
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
        var extra = (targeting?.ExtraTargets ?? 0) + (_psi.Amped(source)?.AmpExtraTargets ?? 0);
        if (targeting != null && extra > 0)
            ChainTargets(source, user, target, targeting, extra);
    }

    private void ChainTargets(EntityUid source, EntityUid user, EntityUid first, CFTargetingComponent targeting, int extra)
    {
        var origin = _transform.GetMapCoordinates(user);
        var reach = Math.Max(1f, targeting.RangeFeet / CFAreaGrid.FeetPerTile);
        var left = extra;
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
        {
            // A spot with no area around it, such as Figment's, still has to be in range.
            if (CompOrNull<CFTargetingComponent>(source)?.Delivery == CFDelivery.Point && !InRange(source, user, aim))
            {
                failure = "cf-spell-out-of-range";
                return false;
            }

            return true;
        }

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
        {
            // No area: the effects happen once, at the spot, with no creature to roll against.
            if (CompOrNull<CFTargetingComponent>(source)?.Delivery != CFDelivery.Point)
                return 0;

            var spot = _maps.TryFindGridAt(aim, out var grid, out _)
                ? _transform.ToCoordinates(grid, aim)
                : _transform.ToCoordinates(aim);
            RunDegreeEffects(source, BuildArgs(source, user, null, spot, null, out _), landed: true);
            return 0;
        }

        // An area that fills with gas, fire or liquid spreads from where it starts, and has its own way of rolling.
        if (_gas.TryStart((source, null), user, aim))
            return 0;

        if (_fills.TryStart(source, user, aim, out var affected))
            return affected;

        var origin = _transform.GetMapCoordinates(user);
        var targets = CollectArea(
            user,
            area.Shape,
            _spellshapes.AreaFeet(source, user, area),
            origin,
            aim,
            area.StartsAtPoint(),
            area.IncludeSelf,
            area.IncludeSelf,
            area.Filter,
            area.Whitelist,
            area.MaxTargets);

        var start = area.StartsAtPoint() ? aim : origin;
        foreach (var target in targets)
        {
            ResolveTarget(source, user, target, start);
        }

        if (targets.Count == 0)
            _popup.PopupEntity(Loc.GetString("cf-spell-no-one"), user, user);

        return targets.Count;
    }

    /// <summary>
    /// The area of the source's chosen mode, centred on the user, or a cone or line aimed at <paramref name="aim"/>. Every
    /// creature in it is resolved. Returns how many there were. Heal and Harm's 3-action version, and Directed Channel.
    /// </summary>
    public int ResolveModeArea(EntityUid source, EntityUid user, CFModeOption mode, MapCoordinates aim)
    {
        if (mode.AreaShape is not { } shape)
            return 0;

        var origin = _transform.GetMapCoordinates(user);
        var targets = CollectArea(
            user,
            shape,
            mode.AreaFeet,
            origin,
            aim,
            atPoint: false,
            includeCenter: true,
            selfAllowed: false,
            mode.AreaFilter);

        foreach (var target in targets)
        {
            ResolveTarget(source, user, target, origin);
        }

        if (targets.Count == 0)
            _popup.PopupEntity(Loc.GetString("cf-spell-no-one"), user, user);

        return targets.Count;
    }

    /// <summary>True when the source is being used in a mode that covers an area, not one creature.</summary>
    public bool IsAreaUse(EntityUid source)
    {
        return HasComp<CFAreaEffectComponent>(source)
               || TryComp(source, out CFModeChoiceComponent? mode) && mode.ChosenOption() is { AreaShape: not null };
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
        if (origin.MapId != aim.MapId || !_maps.TryFindGridAt(origin, out var grid, out var gridComp))
            return new List<EntityUid>();

        var originLocal = _maps.WorldToLocal(grid, gridComp, origin.Position);
        var aimLocal = _maps.WorldToLocal(grid, gridComp, aim.Position);
        var tiles = CFAreaGrid.Collect(shape, sizeFeet, originLocal, aimLocal, gridComp.TileSize, includeCenter);

        var searchFrom = atPoint ? aim : origin;
        var reach = Math.Max(3f, sizeFeet / CFAreaGrid.FeetPerTile + 3f);
        return CollectInTiles(user, (grid, gridComp), tiles, searchFrom, reach, filter, selfAllowed, whitelist, maxTargets);
    }

    /// <summary>
    /// The grid tiles a source's area covers from where the user stands now, with spellshapes counted. False when the
    /// source has no area, or the user is not on a grid.
    /// </summary>
    public bool AreaTiles(
        EntityUid source,
        EntityUid user,
        MapCoordinates aim,
        out Entity<MapGridComponent> grid,
        [NotNullWhen(true)] out HashSet<Vector2i>? tiles)
    {
        grid = default;
        tiles = null;
        var origin = _transform.GetMapCoordinates(user);
        if (!TryComp(source, out CFAreaEffectComponent? area)
            || origin.MapId != aim.MapId
            || !_maps.TryFindGridAt(origin, out var gridUid, out var gridComp))
        {
            return false;
        }

        grid = (gridUid, gridComp);
        tiles = CFAreaGrid.Collect(
            area.Shape,
            _spellshapes.AreaFeet(source, user, area),
            _maps.WorldToLocal(gridUid, gridComp, origin.Position),
            _maps.WorldToLocal(gridUid, gridComp, aim.Position),
            gridComp.TileSize,
            area.IncludeSelf);
        return true;
    }

    /// <summary>
    /// Resolves the source on every creature standing in the given tiles now, with the filter of its area. For an area
    /// that took time to fill, such as a breath that flows out as liquid. Returns how many were affected.
    /// </summary>
    public int ResolveTiles(EntityUid source, EntityUid user, Entity<MapGridComponent> grid, IReadOnlyCollection<Vector2i> tiles)
    {
        var targets = CreaturesInTiles(source, user, grid, tiles);
        foreach (var target in targets)
        {
            ResolveTarget(source, user, target);
        }

        if (targets.Count == 0)
            _popup.PopupEntity(Loc.GetString("cf-spell-no-one"), user, user);

        return targets.Count;
    }

    /// <summary>The creatures standing in the given tiles now that the source's area would affect.</summary>
    public List<EntityUid> CreaturesInTiles(
        EntityUid source,
        EntityUid user,
        Entity<MapGridComponent> grid,
        IReadOnlyCollection<Vector2i> tiles)
    {
        if (!TryComp(source, out CFAreaEffectComponent? area) || tiles.Count == 0)
            return new List<EntityUid>();

        // Search around the middle of the tiles, far enough to reach every one.
        var center = Vector2.Zero;
        foreach (var tile in tiles)
        {
            center += (Vector2) tile;
        }

        center = center / tiles.Count + new Vector2(0.5f, 0.5f);
        var reach = 1.5f;
        foreach (var tile in tiles)
        {
            reach = Math.Max(reach, ((Vector2) tile + new Vector2(0.5f, 0.5f) - center).Length() + 1.5f);
        }

        var searchFrom = _transform.ToMapCoordinates(new EntityCoordinates(grid, center * grid.Comp.TileSize));
        var set = tiles as HashSet<Vector2i> ?? new HashSet<Vector2i>(tiles);
        return CollectInTiles(
            user,
            grid,
            set,
            searchFrom,
            reach * grid.Comp.TileSize,
            area.Filter,
            area.IncludeSelf,
            area.Whitelist,
            area.MaxTargets);
    }

    private List<EntityUid> CollectInTiles(
        EntityUid user,
        Entity<MapGridComponent> gridEnt,
        HashSet<Vector2i> tiles,
        MapCoordinates searchFrom,
        float reach,
        CFTargetFilter filter,
        bool selfAllowed,
        EntityWhitelist? whitelist,
        int maxTargets)
    {
        var found = new List<EntityUid>();
        var (grid, gridComp) = gridEnt;
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

    /// <summary>The next tile over, measured to a creature standing diagonally from the user.</summary>
    private const float TouchFeet = 7.5f;

    [Dependency] private CFAuraSystem _auras = default!;
    [Dependency] private Content.Shared.CF.Cover.CFCoverSystem _cover = default!;
    [Dependency] private CFBreathGasSystem _gas = default!;
    [Dependency] private CFAreaFillSystem _fills = default!;
    [Dependency] private CFPsiSystem _psi = default!;
    [Dependency] private CFTauntSystem _taunts = default!;
    [Dependency] private CFReactionSystem _reactions = default!;
    [Dependency] private CFVfxSystem _vfx = default!;
    [Dependency] private CFTemporaryHpSystem _tempHp = default!;
    [Dependency] private CFStanceSystem _stances = default!;

    /// <summary>
    /// Rolls the source's check against one creature and runs its effect lists.
    /// Concealment and blindness force a flat check first. Returns true when the check landed.
    /// </summary>
    /// <param name="areaOrigin">Where the area this is part of starts. Cover from there adds to a Reflex save.</param>
    public bool ResolveTarget(EntityUid source, EntityUid user, EntityUid target, MapCoordinates? areaOrigin = null)
    {
        if (user != target && !_conditions.PassFlatCheck(user, target))
            return false;

        var check = CompOrNull<CFCheckComponent>(source);
        var args = BuildArgs(source, user, target, Transform(target).Coordinates, check, out var stats);

        // Immune to what this is: a mental effect on a skeleton, poison on a dragon.
        if (user != target && _conditions.IsImmuneToTraits(target, args.Traits))
        {
            _popup.PopupEntity(Loc.GetString("cf-cond-immune"), target);
            return false;
        }

        // A Sorcerer's blood magic goes to the first enemy its bloodline spell reaches.
        EntityManager.System<Content.Shared.CF.Spells.CFBloodMagicSystem>().NoteTarget(user, source, target);

        var amp = _psi.Amped(source);
        if (amp is { AmpOffGuard: true } && user != target)
            _conditions.SetOffGuardTo(target, user, 6f);

        // A taunted creature takes this on the attack or the DC of anything aimed past its taunter.
        var taunted = check != null && check.Defense != CFDefenseType.None ? _taunts.NoteAttack(user, target) : 0;

        var landed = true;
        if (check != null && check.Defense == CFDefenseType.ArmorClass)
        {
            args.Degree = RollAttack(user, target, check, stats.AttackBonus + taunted);

            // Immune to critical hits: the damage of a hit, and here the hit's effects too.
            if (args.Degree == CFDegree.CriticalSuccess && _conditions.ImmuneToCriticalHits(target))
                args.Degree = CFDegree.Success;

            landed = args.Degree.IsSuccess();
            _auras.NoteAttacked(target, user);
        }
        else if (check is { Skill: { } skill, Defense: not CFDefenseType.None })
        {
            // A skill check against the target's defense DC: Goblin Song, Catfolk Dance.
            var ability = _prototypes.TryIndex<CFAbilitySkillPrototype>(skill, out var skillProto) ? skillProto.Ability : CFAbility.Dexterity;
            args.Degree = _conditions.RollSkill(user, target, skill, ability, check.Defense);
            landed = args.Degree.IsSuccess();
        }
        else if (check != null
                 && check.Defense != CFDefenseType.None
                 && (check.HarmedBy is not { } energy || CFVitalEnergies.HarmsFor(EntityManager, user, target, energy)))
        {
            if (HasComp<CFSpellComponent>(source))
                _vfx.Targeted(target, source);

            // Cover from where an area starts is a bonus to Reflex, which is the same as a lower DC.
            var cover = check.Defense == CFDefenseType.Reflex && areaOrigin is { } start && user != target
                ? _cover.ReflexBonus(target, start)
                : 0;

            args.Degree = _conditions.RollDefense(target, check.Defense, stats.Dc + taunted - cover, out _, args.Traits);
            landed = !args.Degree.IsSuccess();

            // Emotional Push: a failed save against the caster's emotion spell leaves the enemy open to it.
            if (landed
                && user != target
                && args.Traits.Contains("Emotion")
                && HasComp<CFSpellComponent>(source)
                && CompOrNull<CFCombatOptionsComponent>(user)?.EmotionalPush == true)
            {
                _conditions.SetOffGuardTo(target, user, 6f);
            }
        }

        RunDegreeEffects(source, args, landed);
        _spellshapes.NoteAffected(user, source, target);
        if (amp != null)
        {
            Run(amp.AmpEffects, args);
            Run(args.Degree switch
                {
                    CFDegree.CriticalSuccess => amp.AmpCritical,
                    CFDegree.Success => amp.AmpSuccess,
                    CFDegree.Failure => amp.AmpFailure,
                    _ => amp.AmpCriticalFailure,
                },
                args);
        }

        // A feat's amp in place of the cantrip's own (Mental Balm, Homing Beacon).
        _psi.ApplyFeatAmp(source, user, target, landed);
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
            Traits = SourceTraits(source),
            Rank = context.Rank,
            HeightenSteps = steps,
            Dc = stats.Dc,
            AbilityModifier = stats.AbilityModifier,
            DamageTypeOverride = _psi.EnergyOverride(source),
            Mode = CompOrNull<CFModeChoiceComponent>(source)?.Chosen ?? 0,
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

        armorClass += _conditions.ArmorClassModifier(target);
        armorClass += _cover.AcBonus(target, user, ranged: true);
        armorClass += _bonuses.AgainstSource(target, CFBonusStat.ArmorClass, user);
        armorClass -= check.TargetAcPenalty;

        attackBonus += check.AttackModifier;
        attackBonus += _bonuses.Total(user, CFBonusStat.AttackRoll);
        attackBonus += _bonuses.AgainstSource(user, CFBonusStat.AttackRoll, target);
        attackBonus -= _conditions.Frightened(user);
        attackBonus += _conditions.CircumstanceAttackPenalty(user, target);
        _conditions.NoteAttackRoll(user, target, melee: false);
        if (check.MultipleAttackPenalty)
        {
            attackBonus += _actions.MapPenalty(user, agile: false);
            _actions.CountAttack(user);
        }

        var d20 = _random.Next(1, 21);
        if (_bonuses.TryConsumeFortune(user))
            d20 = Math.Max(d20, _random.Next(1, 21));

        return AttackDegree(user, target, d20, attackBonus, armorClass);
    }

    /// <summary>A rolled attack's result, after the target's reactions. A spell is not a projectile, so it counts as melee to them.</summary>
    public CFDegree AttackDegree(EntityUid user, EntityUid target, int d20, int attackBonus, int armorClass)
    {
        var degree = CFDegrees.DegreeOfSuccess(d20, attackBonus, armorClass);
        return _reactions.AnswerAttack(target, user, ranged: false, d20, attackBonus, ref armorClass, degree, strike: false);
    }

    /// <summary>A source's traits: those on the entity, and for a spell those on its prototype.</summary>
    public HashSet<ProtoId<CFTraitPrototype>> SourceTraits(EntityUid source)
    {
        var traits = new HashSet<ProtoId<CFTraitPrototype>>();
        if (TryComp(source, out CFTraitComponent? own))
            traits.UnionWith(own.Traits);

        // An action carries its creature's traits too, so a save upgrade against undead counts against a ghoul's spell.
        if (TryComp(source, out ActionComponent? action)
            && action.AttachedEntity is { } creature
            && TryComp(creature, out CFTraitComponent? creatureTraits))
        {
            traits.UnionWith(creatureTraits.Traits);
        }

        if (TryComp(source, out CFSpellComponent? spell))
            traits.Add("Magical");

        if (spell != null && _prototypes.TryIndex(spell.Spell, out CFSpellPrototype? proto))
            traits.UnionWith(proto.Traits);

        return traits;
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
