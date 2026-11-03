using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Spells;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Carpfinder 2e Strikes via stock SS14 melee and projectile hits.
/// </summary>
public sealed partial class CFStrikeSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFCombatBridgeSystem _bridge = default!;
    [Dependency] private CFDyingSystem _dying = default!;
    [Dependency] private CFFlankingSystem _flanking = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>Unconscious / dying creatures are off-guard (−4 AC status, simplified).</summary>
    private const int DownedAcPenalty = 4;

    /// <summary>Stops a reaction Strike from starting another reaction Strike.</summary>
    private int _strikeDepth;

    private bool _inChampion;

    [SubscribeLocalEvent]
    private void OnCfWeaponHit(Entity<CFMeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (!TryGetMeleeAttacker(args, out var combat, out var abilities))
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        var stats = CFStrikeWeaponStats.FromMelee(weapon.Comp);
        if (args.HitEntities.Count == 0)
        {
            _actions.CountAttack(args.User);
            return;
        }

        BeginMeleeStrikes(args.User, args.HitEntities, combat, abilities, stats, weapon.Owner, melee: true);
    }

    [SubscribeLocalEvent]
    private void OnUnarmedMeleeHit(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (HasComp<CFMeleeWeaponComponent>(weapon))
            return;

        if (!IsUnarmedWeapon(args.User, weapon.Owner))
            return;

        if (!TryGetMeleeAttacker(args, out var combat, out var abilities))
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        if (args.HitEntities.Count == 0)
        {
            _actions.CountAttack(args.User);
            return;
        }

        BeginMeleeStrikes(args.User, args.HitEntities, combat, abilities, UnarmedStats(args.User), weapon.Owner, melee: true);
    }

    /// <summary>
    /// Projectile fired from a <see cref="CFRangedWeaponComponent"/> — replace SS14 damage with a PF2e Strike.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBeforeProjectileHit(Entity<ProjectileComponent> projectile, ref BeforeProjectileHitEvent args)
    {
        var shooter = args.Shooter ?? projectile.Comp.Shooter;
        if (shooter != null && !_factions.CanHarm(shooter.Value, args.Target))
        {
            args.Damage = new DamageSpecifier();
            return;
        }

        if (projectile.Comp.Weapon is not { } weaponUid
            || !TryComp(weaponUid, out CFRangedWeaponComponent? ranged))
            return;

        if (shooter == null
            || !TryComp(shooter.Value, out CFCombatantComponent? combat)
            || !TryComp(shooter.Value, out CFAbilityScoreComponent? abilities))
            return;

        // Cancel stock projectile damage (CF Strike applies its own mirrored SS14 damage).
        args.Damage = new DamageSpecifier();

        if (!_net.IsServer)
            return;

        var stats = CFStrikeWeaponStats.FromRanged(ranged);
        var distanceFeet = GetDistanceFeet(shooter.Value, args.Target);
        var map = _actions.MapPenalty(shooter.Value, stats.Agile);
        _actions.CountAttack(shooter.Value);
        ResolveStrike(
            shooter.Value,
            args.Target,
            combat,
            abilities,
            stats,
            distanceFeet,
            ranged.Propulsive,
            weaponUid,
            melee: false,
            map);
        if (_strikeDepth == 0)
            ReactiveStrike(shooter.Value);
    }

    private bool TryGetMeleeAttacker(
        MeleeHitEvent args,
        out CFCombatantComponent combat,
        out CFAbilityScoreComponent abilities)
    {
        combat = default!;
        abilities = default!;

        if (!args.IsHit)
            return false;

        return TryComp(args.User, out combat!) && TryComp(args.User, out abilities!);
    }

    private bool IsUnarmedWeapon(EntityUid user, EntityUid weapon)
    {
        if (weapon == user)
            return true;

        if (_inventory.TryGetSlotEntity(user, "gloves", out var gloves) && gloves == weapon)
            return true;

        if (_hands.IsHolding(user, weapon))
            return false;

        return false;
    }

    private void BeginMeleeStrikes(
        EntityUid user,
        IReadOnlySet<EntityUid> targets,
        CFCombatantComponent combat,
        CFAbilityScoreComponent abilities,
        CFStrikeWeaponStats stats,
        EntityUid weaponUid,
        bool melee)
    {
        // One swing is one attack, even when the arc touches more than one creature.
        var map = _actions.MapPenalty(user, stats.Agile);
        var counted = false;
        foreach (var target in targets)
        {
            // Party members never reach AC, the d20, or damage.
            if (!_factions.CanHarm(user, target))
                continue;

            if (!counted)
            {
                _actions.CountAttack(user);
                counted = true;
            }

            ResolveStrike(user, target, combat, abilities, stats, distanceFeet: 0f, propulsive: false, weaponUid, melee, map);
        }
    }

    /// <summary>
    /// One Strike from an activity or a reaction. The radial action already paid its action cost.
    /// </summary>
    public bool PerformStrike(
        EntityUid user,
        EntityUid target,
        CFStrikeWeaponStats weapon,
        EntityUid weaponUid,
        bool melee,
        int attackPenalty = 0,
        int extraDice = 0,
        bool finisher = false,
        int bonusDamage = 0,
        bool countMap = true,
        int? fixedMap = null)
    {
        if (_strikeDepth > 2)
            return false;

        if (!TryComp(user, out CFCombatantComponent? combat) || !TryComp(user, out CFAbilityScoreComponent? abilities))
            return false;

        var map = fixedMap ?? _actions.MapPenalty(user, weapon.Agile);
        if (countMap)
            _actions.CountAttack(user);

        _strikeDepth++;
        try
        {
            return ResolveStrike(
                user,
                target,
                combat,
                abilities,
                weapon,
                distanceFeet: melee ? 0f : GetDistanceFeet(user, target),
                propulsive: false,
                weaponUid,
                melee,
                map,
                attackPenalty,
                extraDice,
                finisher,
                bonusDamage);
        }
        finally
        {
            _strikeDepth--;
        }
    }

    /// <summary>
    /// A creature with Reactive Strike answers a manipulate action or a ranged Strike made in its reach.
    /// </summary>
    public void ReactiveStrike(EntityUid actor)
    {
        if (!_net.IsServer || _strikeDepth > 0)
            return;

        var origin = _transform.GetMapCoordinates(actor);
        foreach (var uid in _lookup.GetEntitiesInRange(origin, CFMelee.MeleeRange))
        {
            if (uid == actor || !TryComp(uid, out CFCombatOptionsComponent? opt) || !opt.ReactiveStrike)
                continue;

            if (!_factions.CanHarm(uid, actor))
                continue;

            if (!CFFlanking.InReach(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(actor), CFFlanking.DefaultReachFeet))
                continue;

            if (!_conditions.TrySpendReaction(uid, CFReactions.Strike))
                continue;

            if (!TryMeleeWeapon(uid, out var stats, out var weapon))
                continue;

            PerformStrike(uid, actor, stats, weapon, melee: true);
            return;
        }
    }

    public CFStrikeWeaponStats UnarmedStats(EntityUid user)
    {
        if (TryComp(user, out CFCombatOptionsComponent? opt) && opt.NaturalDie > 0)
        {
            var die = opt.PowerfulFist ? Math.Max(opt.NaturalDie, 6) : opt.NaturalDie;
            var dice = Math.Max(1, opt.NaturalDice);
            var type = string.IsNullOrEmpty(opt.NaturalType) ? CFMelee.UnarmedDamageType : opt.NaturalType;
            return new CFStrikeWeaponStats(dice, die, type, CFMelee.UnarmedProficiency, opt.NaturalFinesse, 0, Agile: opt.NaturalAgile);
        }

        var fist = CFStrikeWeaponStats.Unarmed();
        if (TryComp(user, out CFCombatOptionsComponent? monk) && monk.PowerfulFist)
            return fist with { DamageDie = 6 };

        return fist;
    }

    public bool TryMeleeWeapon(EntityUid user, out CFStrikeWeaponStats stats, out EntityUid weaponUid)
    {
        if (_hands.TryGetActiveItem(user, out var held)
            && held != null
            && TryComp(held.Value, out CFMeleeWeaponComponent? melee))
        {
            stats = CFStrikeWeaponStats.FromMelee(melee);
            weaponUid = held.Value;
            return true;
        }

        stats = UnarmedStats(user);
        weaponUid = user;
        return true;
    }

    private bool ResolveStrike(
        EntityUid user,
        EntityUid target,
        CFCombatantComponent attackerCombat,
        CFAbilityScoreComponent attackerAbilities,
        CFStrikeWeaponStats weapon,
        float distanceFeet,
        bool propulsive,
        EntityUid weaponUid,
        bool melee,
        int mapPenalty = 0,
        int attackPenalty = 0,
        int extraDice = 0,
        bool finisher = false,
        int bonusDamage = 0)
    {
        if (!_factions.CanHarm(user, target))
            return false;

        if (_conditions.IsCalmed(user) || _conditions.IsCharmedAgainst(user, target))
        {
            _popup.PopupEntity(Loc.GetString("cf-cond-cannot-harm"), user, user);
            return false;
        }

        if (!_bridge.TryEnsureCombatTarget(target, out var defender, out var hardness, out var isObject))
        {
            _popup.PopupEntity(Loc.GetString("cf-combat-strike-no-combatant"), user, user);
            return false;
        }

        // Objects at 0 HP are already destroyed; creatures at 0 HP are dying / unconscious and can be finished.
        var alreadyDown = !isObject && _dying.IsDown(defender);
        if (isObject && defender.CurrentHp <= 0)
            return false;

        if (!isObject && TryComp(target, out MobStateComponent? mobState) && _mobState.IsDead(target, mobState))
            return false;

        _conditions.NoteAttack(user);
        if (!isObject && TryComp(target, out CFConditionComponent? wardCond) && wardCond.Sanctuary)
        {
            var will = CFDefense.SaveModifier(
                _prototypes,
                attackerCombat,
                attackerAbilities,
                CFSpellDefense.Will,
                0,
                _conditions.Frightened(user));
            var save = CFDefense.Roll(_random, will, wardCond.SanctuaryDc);
            if (save is CFStrikeDegree.Failure or CFStrikeDegree.CriticalFailure)
            {
                _popup.PopupEntity(Loc.GetString("cf-cond-sanctuary-held"), user, user);
                return false;
            }

            wardCond.Sanctuary = false;
            Dirty(target, wardCond);
        }

        if (!isObject && !_conditions.PassFlatCheck(user, target))
        {
            RaiseNetworkEvent(
                new CFCombatNumberEvent(GetNetEntity(target), CFStrikeDegree.Failure, 0, weapon.DamageType),
                Filter.Pvs(target));
            return false;
        }

        var ignoreVolley = false;
        var pointBlank = 0;
        TryComp(user, out CFCombatOptionsComponent? style);
        if (weapon.Ranged && style is { PointBlank: true })
        {
            var first = weapon.RangeIncrementFeet > 0 ? weapon.RangeIncrementFeet : 30;
            if (distanceFeet <= first)
            {
                ignoreVolley = true;
                pointBlank = 2;
            }
        }

        var weaponTrained = IsWeaponTrained(attackerCombat, weapon.Proficiency);
        var attackBonus = mapPenalty + attackPenalty + CFMelee.AttackBonus(
            attackerAbilities,
            attackerCombat,
            weapon,
            weaponTrained,
            distanceFeet,
            ignoreVolley);
        attackBonus -= _conditions.Frightened(user);

        // Beyond sixth range increment: automatic miss.
        if (weapon.Ranged && attackBonus <= -900)
        {
            RaiseNetworkEvent(
                new CFCombatNumberEvent(GetNetEntity(target), CFStrikeDegree.Failure, 0, weapon.DamageType),
                Filter.Pvs(target));
            return false;
        }

        // Downed creatures already take a larger AC penalty. Off-guard does not stack with itself.
        var acPenalty = 0;
        if (alreadyDown)
            acPenalty = DownedAcPenalty;
        else if (melee && !isObject && (_flanking.IsOffGuardTo(target, user) || _conditions.IsOffGuardTo(target, user)))
            acPenalty = CFFlanking.OffGuardAcPenalty;

        var targetAc = defender.ArmorClass - acPenalty - _conditions.Frightened(target);
        if (TryComp(target, out CFSpellStatusComponent? ward))
            targetAc += ward.AcBonus;

        if (TryComp(target, out CFConditionComponent? targetCond) && targetCond.RaisedShield)
            targetAc += 2;

        if (TryComp(target, out CFCombatOptionsComponent? defenderStyle) && defenderStyle.Raging)
            targetAc -= 1;

        var rolled = weapon;
        if (extraDice > 0)
            rolled = rolled with { DamageDice = rolled.DamageDice + extraDice };

        if (TryComp(user, out CFSpellStatusComponent? rune) && rune.ExtraWeaponDie > 0)
            rolled = rolled with { DamageDice = rolled.DamageDice + rune.ExtraWeaponDie };

        var damageMod = CFMelee.DamageBonus(attackerAbilities, rolled, propulsive) + pointBlank;
        if (melee && style is { Raging: true })
            damageMod += 2;

        if (melee && TryComp(user, out CFConditionComponent? grown))
            damageMod += grown.EnlargeDamage;

        if (style != null && style.Exploited == target)
            damageMod += 2;

        var d20 = _random.Next(1, 21);
        if (TryComp(user, out CFSpellStatusComponent? boon))
            attackBonus += boon.AttackBonus;

        if (style != null && style.StratagemTarget == target && style.StratagemRoll > 0)
        {
            d20 = style.StratagemRoll;
            style.StratagemRoll = 0;
            style.StratagemTarget = null;
            Dirty(user, style);
        }
        else if (boon is { TrueStrike: true })
        {
            d20 = Math.Max(d20, _random.Next(1, 21));
            boon.TrueStrike = false;
        }

        var degree = CFMelee.DegreeOfSuccess(d20, attackBonus, targetAc);
        if (!isObject)
            degree = AdjustDefense(target, user, d20, attackBonus, ref targetAc, degree);

        var damage = 0;
        var criticalHit = degree == CFStrikeDegree.CriticalSuccess;
        var hit = degree is CFStrikeDegree.Success or CFStrikeDegree.CriticalSuccess;

        if (hit)
        {
            if (alreadyDown)
            {
                // Damage while dying / unconscious at 0 HP increases dying (can kill at dying 4).
                _dying.ApplyDamageWhileDown(target, defender, criticalHit, user);
            }
            else
            {
                damage = CFMelee.RollStrikeDamage(
                    _random,
                    rolled,
                    damageMod,
                    criticalHit);

                damage += Precision(user, target, style, melee, isObject, criticalHit);
                damage += bonusDamage;
                damage = _conditions.Resist(target, rolled.DamageType, damage);
                damage = ApplyShieldBlock(target, damage);

                if (isObject && hardness > 0)
                    damage = Math.Max(0, damage - hardness);

                if (damage > 0)
                {
                    defender.CurrentHp = Math.Max(0, defender.CurrentHp - damage);
                    Dirty(target, defender);
                    MirrorToSs14(user, target, defender, damage, rolled.DamageType, isObject);
                }

                if (!isObject && defender.CurrentHp <= 0)
                {
                    _popup.PopupEntity(Loc.GetString("cf-combat-strike-defeated"), target, user);
                    _dying.KnockOut(target, defender, criticalHit, user);
                    YoureNext(user, target);
                }
                else if (isObject && defender.CurrentHp <= 0)
                {
                    _popup.PopupEntity(Loc.GetString("cf-combat-strike-defeated"), target, user);
                    DestroyObject(user, target);
                }
            }
        }
        else if (finisher && style is { PanacheStyle: true })
        {
            damage = 2;
            if (!isObject && !alreadyDown)
            {
                defender.CurrentHp = Math.Max(0, defender.CurrentHp - damage);
                Dirty(target, defender);
                MirrorToSs14(user, target, defender, damage, "precision", isObject);
                if (defender.CurrentHp <= 0)
                {
                    _dying.KnockOut(target, defender, fromCriticalHit: false, user);
                    YoureNext(user, target);
                }
            }
        }

        if (hit && !isObject && !_inChampion)
            ChampionReact(user, target);

        PlayStrikeImpact(weaponUid, target, degree, rolled.DamageType, melee);

        RaiseNetworkEvent(
            new CFCombatNumberEvent(GetNetEntity(target), degree, damage, rolled.DamageType),
            Filter.Pvs(target));
        return hit;
    }

    /// <summary>
    /// Raises a shield, or dodges, when that would answer an attack that is about to land.
    /// Each spends the creature's one reaction.
    /// </summary>
    private CFStrikeDegree AdjustDefense(
        EntityUid target,
        EntityUid user,
        int d20,
        int attackBonus,
        ref int targetAc,
        CFStrikeDegree degree)
    {
        _ = user;
        if (degree is not (CFStrikeDegree.Success or CFStrikeDegree.CriticalSuccess))
            return degree;

        if (TryComp(target, out CFConditionComponent? cond)
            && !cond.RaisedShield
            && _hands.TryGetActiveItem(target, out var held)
            && held != null
            && HasComp<CFShieldComponent>(held.Value)
            && _conditions.TrySpendReaction(target, CFReactions.Raise))
        {
            _conditions.RaiseShield(target);
            targetAc += 2;
            degree = CFMelee.DegreeOfSuccess(d20, attackBonus, targetAc);
            if (degree is not (CFStrikeDegree.Success or CFStrikeDegree.CriticalSuccess))
                return degree;
        }

        if (_conditions.TrySpendReaction(target, CFReactions.Nimble))
        {
            targetAc += 2;
            degree = CFMelee.DegreeOfSuccess(d20, attackBonus, targetAc);
        }

        return degree;
    }

    private int Precision(
        EntityUid user,
        EntityUid target,
        CFCombatOptionsComponent? style,
        bool melee,
        bool isObject,
        bool critical)
    {
        _ = critical;
        if (style == null || isObject || !melee)
            return 0;

        var total = 0;
        var offGuard = _flanking.IsOffGuardTo(target, user) || _conditions.IsOffGuardTo(target, user);
        if (style.SneakAttack && offGuard)
        {
            var level = TryComp(user, out CFCombatantComponent? combat) ? combat.Level : 1;
            var sneak = Math.Max(1, (level + 3) / 4);
            for (var i = 0; i < sneak; i++)
                total += _random.Next(1, 7);
        }

        if (style.Prey == target && !style.PreyHitThisTurn)
        {
            total += _random.Next(1, 9);
            style.PreyHitThisTurn = true;
            Dirty(user, style);
        }

        if (style.Panache && style.PanacheStyle)
            total += 2;

        return Math.Max(0, total);
    }

    private int ApplyShieldBlock(EntityUid target, int damage)
    {
        var spellShield = TryComp(target, out CFConditionComponent? cond) && cond.ShieldHardness > 0;
        damage = _conditions.BlockWithSpell(target, damage);
        if (damage <= 0 || spellShield)
            return Math.Max(0, damage);

        if (cond == null || !cond.RaisedShield)
            return damage;

        if (!_conditions.TrySpendReaction(target, CFReactions.Block))
            return damage;

        var hardness = 5;
        if (_hands.TryGetActiveItem(target, out var held)
            && held != null
            && TryComp(held.Value, out CFShieldComponent? shield))
            hardness = shield.Hardness;

        var blocked = Math.Min(damage, hardness);
        if (blocked > 0)
            _popup.PopupEntity(Loc.GetString("cf-act-shield-block", ("amount", blocked)), target);

        return damage - blocked;
    }

    private void ChampionReact(EntityUid attacker, EntityUid victim)
    {
        if (_inChampion)
            return;

        _inChampion = true;
        var origin = _transform.GetMapCoordinates(victim);
        foreach (var uid in _lookup.GetEntitiesInRange(origin, 3f))
        {
            if (uid == victim || uid == attacker || !TryComp(uid, out CFCombatOptionsComponent? opt) || !opt.Champion)
                continue;

            if (_factions.CanHarm(uid, victim) || !_factions.CanHarm(uid, attacker))
                continue;

            if (!CFFlanking.InReach(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(attacker), CFFlanking.DefaultReachFeet))
                continue;

            if (!_conditions.TrySpendReaction(uid, CFReactions.Champion))
                continue;

            if (!TryMeleeWeapon(uid, out var stats, out var weapon))
                continue;

            PerformStrike(uid, attacker, stats, weapon, melee: true);
            _inChampion = false;
            return;
        }

        _inChampion = false;
    }

    private void YoureNext(EntityUid user, EntityUid downed)
    {
        if (!TryComp(user, out CFCombatOptionsComponent? opt) || !opt.YoureNext || opt.ReactionSpent != 0)
            return;

        var here = _transform.GetMapCoordinates(user);
        foreach (var uid in _lookup.GetEntitiesInRange(here, 6f))
        {
            if (uid == user || uid == downed || !HasComp<MobStateComponent>(uid) || !_factions.CanHarm(user, uid))
                continue;

            if (!_conditions.TrySpendReaction(user, CFReactions.Next))
                return;

            _conditions.TryDemoralize(user, uid, 2);
            return;
        }
    }

    /// <summary>
    /// A miss plays the weapon swing at the target, so it does not use the hit noise.
    /// A hit plays <c>soundHit</c>, or a slash/smash default when the weapon has none.
    /// </summary>
    private void PlayStrikeImpact(EntityUid weaponUid, EntityUid target, CFStrikeDegree degree, string cfDamageType, bool melee)
    {
        if (!melee || !TryComp(weaponUid, out MeleeWeaponComponent? weapon))
            return;

        var sound = degree is CFStrikeDegree.Failure or CFStrikeDegree.CriticalFailure
            ? weapon.SwingSound
            : weapon.HitSound ?? CFMeleeAudio.HitSound(cfDamageType);

        _audio.PlayPvs(sound, Transform(target).Coordinates, sound.Params.WithVariation(0.05f));
    }

    private float GetDistanceFeet(EntityUid from, EntityUid to)
    {
        var a = _transform.GetWorldPosition(from);
        var b = _transform.GetWorldPosition(to);
        var tiles = Vector2.Distance(a, b);
        return CFMelee.TilesToFeet(tiles);
    }

    private bool IsWeaponTrained(CFCombatantComponent attackerCombat, string proficiency)
    {
        if (attackerCombat.ClassId != null
            && _prototypes.TryIndex(attackerCombat.ClassId, out CFClassPrototype? cls))
        {
            return CFAttributeMath.IsWeaponTrained(cls, proficiency);
        }

        return true;
    }

    private void MirrorToSs14(
        EntityUid user,
        EntityUid target,
        CFCombatantComponent defender,
        int cfDamage,
        string damageType,
        bool isObject)
    {
        if (!isObject)
        {
            // CF HP is the source of truth. The SS14 bar is rewritten from it, under the Critical threshold.
            _dying.SyncHealthBar(target, defender);
            return;
        }

        var ss14Type = ToSs14DamageType(damageType);
        var amount = (FixedPoint2) cfDamage;

        if (amount <= 0)
            return;

        var spec = new DamageSpecifier();
        spec.DamageDict[ss14Type] = amount;
        _damageable.TryChangeDamage(target, spec, ignoreResistances: true, origin: user);
    }

    private void DestroyObject(EntityUid user, EntityUid target)
    {
        var finish = new DamageSpecifier();
        finish.DamageDict["Blunt"] = 10000;
        _damageable.TryChangeDamage(target, finish, ignoreResistances: true, origin: user);

        if (!Deleted(target) && !EntityManager.IsQueuedForDeletion(target))
        {
            _destructible.BreakEntity(target);
            if (!Deleted(target) && !EntityManager.IsQueuedForDeletion(target))
                _destructible.DestroyEntity(target);
        }
    }

    private static string ToSs14DamageType(string pf2eType)
    {
        return pf2eType.ToLowerInvariant() switch
        {
            "slashing" or "slash" => "Slash",
            "piercing" => "Piercing",
            "bludgeoning" or "blunt" => "Blunt",
            _ => "Blunt",
        };
    }
}
