using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Effects;
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
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
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
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFWeaponSystem _weapons = default!;
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private CFStealthSystem _stealth = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ThrownItemSystem _thrownItems = default!;

    /// <summary>Unconscious / dying creatures are off-guard (−4 AC status, simplified).</summary>
    private const int DownedAcPenalty = 4;

    /// <summary>Only has to outlast the Strike that gives the attacker away.</summary>
    private const float UnseenOffGuardSeconds = 1f;

    /// <summary>
    /// How much longer a thrown weapon stays in the air than stock's timing. Stock ignores air friction,
    /// so without this a long throw comes down short of a target right under the cursor.
    /// </summary>
    private const float ThrownFlightStretch = 1.2f;

    /// <summary>Stops a reaction Strike from starting another reaction Strike.</summary>
    private int _strikeDepth;

    private bool _inChampion;

    [SubscribeLocalEvent]
    private void OnCfWeaponHit(Entity<CFMeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (!TryGetMeleeAttacker(args, out var combat, out var abilities))
            return;

        if (!_weapons.TryStats(weapon, out var stats))
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        if (args.HitEntities.Count == 0)
        {
            _actions.CountAttack(args.User);
            return;
        }

        if (TryFeatSwing(args, stats, weapon.Owner))
            return;

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

        var fist = UnarmedStats(args.User);
        if (TryFeatSwing(args, fist, weapon.Owner))
            return;

        BeginMeleeStrikes(args.User, args.HitEntities, combat, abilities, fist, weapon.Owner, melee: true);
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
            || !HasComp<CFRangedWeaponComponent>(weaponUid)
            || !_weapons.TryStats(weaponUid, out var stats))
            return;

        if (shooter == null
            || !TryComp(shooter.Value, out CFCombatantComponent? combat)
            || !TryComp(shooter.Value, out CFAbilityScoreComponent? abilities))
            return;

        // Cancel stock projectile damage (CF Strike applies its own mirrored SS14 damage).
        args.Damage = new DamageSpecifier();

        if (!_net.IsServer)
            return;

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
            weaponUid,
            melee: false,
            map);
        if (_strikeDepth == 0)
            ReactiveStrike(shooter.Value);
    }

    [SubscribeLocalEvent]
    private void OnThrown(Entity<CFWeaponComponent> weapon, ref ThrownEvent args)
    {
        if (_weapons.ThrownIncrement(weapon) <= 0
            || !TryComp(weapon, out ThrownItemComponent? thrown)
            || thrown.ThrownTime is not { } start
            || thrown.LandTime is not { } land)
            return;

        thrown.LandTime = start + (land - start) * ThrownFlightStretch;
        Dirty(weapon, thrown);
    }

    /// <summary>
    /// A weapon with the thrown trait that hits a creature makes a ranged Strike with its thrown range increment.
    /// Thrown Strikes add Strength to damage. The action was paid when it left the hand.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnThrownHit(Entity<CFWeaponComponent> weapon, ref ThrowDoHitEvent args)
    {
        if (!_net.IsServer || args.Component.Thrower is not { } thrower || thrower == args.Target)
            return;

        if (!TryComp(thrower, out CFCombatantComponent? combat) || !TryComp(thrower, out CFAbilityScoreComponent? abilities))
            return;

        if (!_weapons.TryStats(weapon, out var stats, thrown: true) || stats.RangeIncrementFeet <= 0)
            return;

        // Only a living enemy stops it. It flies on past an ally or a body.
        if (!TryComp(args.Target, out MobStateComponent? mob)
            || _mobState.IsDead(args.Target, mob)
            || !_factions.CanHarm(thrower, args.Target))
            return;

        var map = _actions.MapPenalty(thrower, stats.Agile);
        _actions.CountAttack(thrower);
        var hit = ResolveStrike(thrower, args.Target, combat, abilities, stats, GetDistanceFeet(thrower, args.Target), weapon, melee: false, map);
        if (_strikeDepth == 0)
            ReactiveStrike(thrower);

        // One throw is one Strike, so the flight ends here. A miss skids on past the target.
        if (TryComp(weapon, out PhysicsComponent? body))
            _thrownItems.LandComponent(weapon, args.Component, body, playSound: !hit);

        _thrownItems.StopThrow(weapon, args.Component);

        // Stock embeds on any touch, so spears turn that off and stick here, only in what the Strike hit.
        if (hit && !TerminatingOrDeleted(args.Target))
        {
            var stick = new ProjectileHitEvent(new DamageSpecifier(), args.Target, thrower);
            RaiseLocalEvent(weapon, ref stick);
        }
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
        var exacting = targets.Count == 1;
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

            ResolveStrike(user, target, combat, abilities, stats, distanceFeet: 0f, weaponUid, melee, map, riders: true, exacting: exacting);
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
        int? fixedMap = null,
        bool skipPrecision = false)
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
                weaponUid,
                melee,
                map,
                attackPenalty,
                extraDice,
                finisher,
                bonusDamage,
                skipPrecision: skipPrecision);
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

    /// <summary>
    /// Fists (1d4 bludgeoning: agile, finesse, nonlethal, unarmed), or the creature's natural attack
    /// from <see cref="CFCombatOptionsComponent"/>, which is lethal and has only the traits it lists.
    /// </summary>
    public CFStrikeWeaponStats UnarmedStats(EntityUid user)
    {
        var proficiency = CFWeaponCategory.Unarmed.Proficiency();
        TryComp(user, out CFCombatOptionsComponent? opt);
        if (opt is { NaturalDie: > 0 })
        {
            var die = opt.PowerfulFist ? Math.Max(opt.NaturalDie, 6) : opt.NaturalDie;
            var dice = Math.Max(1, opt.NaturalDice);
            var type = string.IsNullOrEmpty(opt.NaturalType) ? CFMelee.UnarmedDamageType : opt.NaturalType;
            var traits = new CFWeaponTraits { Agile = opt.NaturalAgile, Finesse = opt.NaturalFinesse, Unarmed = true };
            return new CFStrikeWeaponStats(dice, die, type, proficiency, traits);
        }

        // Powerful fist: d6 fists that are not nonlethal.
        if (opt is { PowerfulFist: true })
        {
            var traits = CFWeaponTraits.Fist with { Nonlethal = false };
            return new CFStrikeWeaponStats(CFMelee.UnarmedDamageDice, 6, CFMelee.UnarmedDamageType, proficiency, traits);
        }

        return new CFStrikeWeaponStats(
            CFMelee.UnarmedDamageDice,
            CFMelee.UnarmedDamageDie,
            CFMelee.UnarmedDamageType,
            proficiency,
            CFWeaponTraits.Fist);
    }

    /// <summary>
    /// The attack bonus before Multiple Attack Penalty and anything spent by rolling, such as Aid.
    /// </summary>
    private int StandingAttackBonus(
        EntityUid user,
        CFCombatantComponent combat,
        CFAbilityScoreComponent abilities,
        CFStrikeWeaponStats weapon,
        float distanceFeet,
        bool ignoreVolley)
    {
        var bonus = CFMelee.AttackBonus(abilities, combat, weapon, _weapons.Rank(combat, weapon), distanceFeet, ignoreVolley);
        bonus -= _conditions.Frightened(user);
        bonus += _conditions.ProneAttackPenalty(user);
        if (weapon.Ranged)
            bonus -= _conditions.Clumsy(user);

        // Item bonuses do not stack: weapon potency or a spell's item bonus, whichever is higher.
        var otherItem = _bonuses.TotalOfType(user, CFBonusStat.AttackRoll, CFBonusType.Item);
        return bonus + _bonuses.Total(user, CFBonusStat.AttackRoll) - otherItem + Math.Max(otherItem, weapon.Potency);
    }

    /// <summary>The AC this attacker has to beat right now.</summary>
    private int ArmorClassAgainst(
        EntityUid user,
        EntityUid target,
        CFCombatantComponent defender,
        bool melee,
        bool unseen,
        bool isObject,
        bool alreadyDown)
    {
        // Downed creatures already take a larger AC penalty. Off-guard does not stack with itself.
        var acPenalty = 0;
        if (alreadyDown)
            acPenalty = DownedAcPenalty;
        else if (!isObject && _conditions.IsProne(target))
            acPenalty = CFFlanking.OffGuardAcPenalty;
        else if ((melee || unseen) && !isObject && (_flanking.IsOffGuardTo(target, user) || _conditions.IsOffGuardTo(target, user)))
            acPenalty = CFFlanking.OffGuardAcPenalty;

        return defender.ArmorClass - acPenalty + _conditions.ArmorClassModifier(target);
    }

    /// <summary>
    /// What a melee Strike at this target would roll with right now. Nothing is rolled, spent or changed, so code with
    /// no player can weigh a Strike up before making it.
    /// </summary>
    public bool TryPreviewMelee(EntityUid user, EntityUid target, out CFStrikePreview preview)
    {
        preview = default;
        if (!TryComp(user, out CFCombatantComponent? combat)
            || !TryComp(user, out CFAbilityScoreComponent? abilities)
            || !TryComp(target, out CFCombatantComponent? defender)
            || !TryMeleeWeapon(user, out var weapon, out _))
            return false;

        preview = new CFStrikePreview(
            StandingAttackBonus(user, combat, abilities, weapon, 0f, false),
            _actions.MapPenalty(user, weapon.Agile),
            ArmorClassAgainst(user, target, defender, melee: true, unseen: false, isObject: false, alreadyDown: false),
            weapon.DamageDice * (weapon.DamageDie + 1) / 2f + CFMelee.DamageBonus(abilities, weapon, combat));
        return true;
    }

    public bool TryMeleeWeapon(EntityUid user, out CFStrikeWeaponStats stats, out EntityUid weaponUid)
    {
        if (_hands.TryGetActiveItem(user, out var held)
            && held != null
            && HasComp<CFMeleeWeaponComponent>(held.Value)
            && _weapons.TryStats(held.Value, out stats))
        {
            weaponUid = held.Value;
            return true;
        }

        if (HasComp<CFMeleeWeaponComponent>(user) && _weapons.TryStats(user, out stats))
        {
            weaponUid = user;
            return true;
        }

        stats = UnarmedStats(user);
        weaponUid = user;
        return true;
    }

    /// <summary>
    /// Multiple Attack Penalty on the next Strike with what is in hand. A held bow counts here,
    /// where <see cref="TryMeleeWeapon"/> passes over it for fists.
    /// </summary>
    public int NextStrikeMapPenalty(EntityUid user)
    {
        if (_hands.TryGetActiveItem(user, out var held) && _weapons.TryStats(held.Value, out var stats))
            return _actions.MapPenalty(user, stats.Agile);

        TryMeleeWeapon(user, out stats, out _);
        return _actions.MapPenalty(user, stats.Agile);
    }

    /// <summary>
    /// Versatile: the type the target is weakest to, or the weapon's own type when nothing is better.
    /// </summary>
    private string ChooseDamageType(EntityUid target, CFStrikeWeaponStats weapon)
    {
        var best = weapon.DamageType;
        var bestDamage = _bonuses.ModifyDamage(target, best, 10);
        foreach (var type in weapon.Traits.Versatile)
        {
            var damage = _bonuses.ModifyDamage(target, type, 10);
            if (damage <= bestDamage)
                continue;

            best = type;
            bestDamage = damage;
        }

        return best;
    }

    /// <summary>Attacks this creature had already made this turn, from its multiple attack penalty.</summary>
    private static int AttacksBefore(int mapPenalty)
    {
        return mapPenalty switch
        {
            0 => 0,
            >= -5 => 1,
            _ => 2,
        };
    }

    private bool ResolveStrike(
        EntityUid user,
        EntityUid target,
        CFCombatantComponent attackerCombat,
        CFAbilityScoreComponent attackerAbilities,
        CFStrikeWeaponStats weapon,
        float distanceFeet,
        EntityUid weaponUid,
        bool melee,
        int mapPenalty = 0,
        int attackPenalty = 0,
        int extraDice = 0,
        bool finisher = false,
        int bonusDamage = 0,
        bool riders = false,
        bool exacting = false,
        bool skipPrecision = false)
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

        // A creature that cannot see its attacker is off-guard to this attack. Then everyone sees the attacker.
        var unseen = !isObject && _stealth.IsUnseenBy(user, target);
        if (unseen)
            _conditions.SetOffGuardTo(target, user, UnseenOffGuardSeconds);

        _stealth.Reveal(user);

        if (!isObject && TryComp(target, out CFConditionComponent? wardCond) && wardCond.Sanctuary)
        {
            var save = _conditions.RollDefense(user, CFDefenseType.Will, wardCond.SanctuaryDc, out _);
            if (save is CFDegree.Failure or CFDegree.CriticalFailure)
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
                new CFCombatNumberEvent(GetNetEntity(target), CFDegree.Failure, 0, weapon.DamageType),
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

        var attackBonus = mapPenalty + attackPenalty + _conditions.TakeAid(user)
                          + StandingAttackBonus(user, attackerCombat, attackerAbilities, weapon, distanceFeet, ignoreVolley);

        // Sweep: +1 circumstance when this turn's earlier attack went at a different creature.
        var attacksBefore = AttacksBefore(mapPenalty);
        TryComp(user, out CFActionPoolComponent? pool);
        if (weapon.Traits.Sweep && attacksBefore > 0 && pool?.LastStrikeTarget is { } last && last != target)
            attackBonus += 1;

        if (pool != null)
            pool.LastStrikeTarget = target;

        // Beyond sixth range increment: automatic miss.
        if (weapon.Ranged && attackBonus <= -900)
        {
            RaiseNetworkEvent(
                new CFCombatNumberEvent(GetNetEntity(target), CFDegree.Failure, 0, weapon.DamageType),
                Filter.Pvs(target));
            return false;
        }

        var targetAc = ArmorClassAgainst(user, target, defender, melee, unseen, isObject, alreadyDown);

        var rolled = weapon with { DamageType = ChooseDamageType(target, weapon) };
        if (extraDice > 0)
            rolled = rolled with { DamageDice = rolled.DamageDice + extraDice };

        // Runic weapon (a spell) and a striking rune do not stack: the higher number of extra dice applies.
        var spellDice = _bonuses.Total(user, CFBonusStat.WeaponDice);
        if (spellDice > weapon.Striking)
            rolled = rolled with { DamageDice = rolled.DamageDice + spellDice - weapon.Striking, Striking = spellDice };

        var damageMod = CFMelee.DamageBonus(attackerAbilities, rolled, attackerCombat) + pointBlank;
        damageMod += CFMelee.ForcefulBonus(rolled, attacksBefore);
        if (melee && style is { Raging: true })
            damageMod += 2;

        damageMod += _bonuses.Total(user, CFBonusStat.Damage, melee ? CFBonusStat.MeleeDamage : null);

        if (style != null && style.Exploited == target)
            damageMod += 2;

        var d20 = _random.Next(1, 21);

        if (style != null && style.StratagemTarget == target && style.StratagemRoll > 0)
        {
            d20 = style.StratagemRoll;
            style.StratagemRoll = 0;
            style.StratagemTarget = null;
            Dirty(user, style);
        }
        else if (_bonuses.TryConsumeFortune(user))
        {
            d20 = Math.Max(d20, _random.Next(1, 21));
        }

        var degree = CFDegrees.DegreeOfSuccess(d20, attackBonus, targetAc);
        if (!isObject)
            degree = AdjustDefense(target, user, d20, attackBonus, ref targetAc, degree);

        var damage = 0;
        var criticalHit = degree == CFDegree.CriticalSuccess;
        var hit = degree is CFDegree.Success or CFDegree.CriticalSuccess;

        if (hit)
        {
            if (alreadyDown)
            {
                // Damage while dying / unconscious at 0 HP increases dying (can kill at dying 4).
                _dying.ApplyDamageWhileDown(target, defender, criticalHit, user);
            }
            else
            {
                var extra = bonusDamage;
                if (!skipPrecision)
                    extra += Precision(user, target, style, melee, isObject, criticalHit);

                if (weapon.Traits.Backstabber && !isObject
                    && (_flanking.IsOffGuardTo(target, user) || _conditions.IsOffGuardTo(target, user)))
                    extra += weapon.Potency >= 3 ? 2 : 1;

                damage = CFMelee.RollStrikeDamage(_random, rolled, damageMod, criticalHit, extra);
                damage = _bonuses.ModifyDamage(target, rolled.DamageType, damage);
                damage = ApplyShieldBlock(target, damage);

                if (isObject && hardness > 0)
                    damage = Math.Max(0, damage - hardness);

                if (damage > 0)
                {
                    defender.CurrentHp = Math.Max(0, defender.CurrentHp - damage);
                    Dirty(target, defender);
                    MirrorToSs14(user, target, defender, damage, rolled.DamageType, isObject);
                    RaiseDamaged(target, user, damage);
                }

                if (!isObject && defender.CurrentHp <= 0)
                {
                    _popup.PopupEntity(Loc.GetString("cf-combat-strike-defeated"), target, user);
                    _dying.KnockOut(target, defender, criticalHit, user);
                    if (weapon.Traits.Nonlethal)
                        _dying.KnockOutNonlethal(target, defender);
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
                RaiseDamaged(target, user, damage);
                if (defender.CurrentHp <= 0)
                {
                    _dying.KnockOut(target, defender, fromCriticalHit: false, user);
                    YoureNext(user, target);
                }
            }
        }

        if (hit && !alreadyDown)
            ApplyPropertyRunes(user, target, weaponUid, degree);

        if (riders)
            ApplyRiders(user, target, style, degree, mapPenalty, weaponUid, isObject, exacting);

        if (hit && !isObject && !_inChampion)
            ChampionReact(user, target);

        PlayStrikeImpact(weaponUid, target, degree, rolled.DamageType, melee || weapon.ThrownStrike);

        RaiseNetworkEvent(
            new CFCombatNumberEvent(GetNetEntity(target), degree, damage, rolled.DamageType),
            Filter.Pvs(target));
        return hit;
    }

    /// <summary>
    /// Raises a shield, or dodges, when that would answer an attack that is about to land.
    /// Each spends the creature's one reaction.
    /// </summary>
    private CFDegree AdjustDefense(
        EntityUid target,
        EntityUid user,
        int d20,
        int attackBonus,
        ref int targetAc,
        CFDegree degree)
    {
        _ = user;
        if (degree is not (CFDegree.Success or CFDegree.CriticalSuccess))
            return degree;

        if (!_shields.TryGetRaisedShield(target, out _)
            && _shields.TryGetUsableShield(target, out var unraised)
            && _conditions.TrySpendReaction(target, CFReactions.Raise))
        {
            _shields.SetRaised(unraised, true);
            targetAc += _shields.RaisedBonus(target);
            degree = CFDegrees.DegreeOfSuccess(d20, attackBonus, targetAc);
            if (degree is not (CFDegree.Success or CFDegree.CriticalSuccess))
                return degree;
        }

        if (_conditions.TrySpendReaction(target, CFReactions.Nimble))
        {
            targetAc += 2;
            degree = CFDegrees.DegreeOfSuccess(d20, attackBonus, targetAc);
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

        if (!_shields.TryGetRaisedShield(target, out var shield))
            return damage;

        if (!_conditions.TrySpendReaction(target, CFReactions.Block))
            return damage;

        return _shields.Block(target, shield, damage);
    }

    /// <summary>
    /// Property runes on the weapon run their effects on the target, with this Strike's degree,
    /// so a damage effect doubles on a critical hit.
    /// </summary>
    private void ApplyPropertyRunes(EntityUid user, EntityUid target, EntityUid weaponUid, CFDegree degree)
    {
        if (!Exists(target) || weaponUid == user)
            return;

        var args = new CFEffectArgs
        {
            User = user,
            Source = weaponUid,
            Target = target,
            Position = Transform(target).Coordinates,
            Degree = degree,
            Defense = CFDefenseType.ArmorClass,
        };

        foreach (var rune in _weapons.PropertyRunes(weaponUid))
        {
            _effects.Run(rune.OnHit, args);
            if (degree == CFDegree.CriticalSuccess)
                _effects.Run(rune.OnCriticalHit, args);
        }
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
    private void PlayStrikeImpact(EntityUid weaponUid, EntityUid target, CFDegree degree, string cfDamageType, bool melee)
    {
        if (!melee || !TryComp(weaponUid, out MeleeWeaponComponent? weapon))
            return;

        var sound = degree is CFDegree.Failure or CFDegree.CriticalFailure
            ? weapon.SwingSound
            : weapon.HitSound ?? CFMeleeAudio.HitSound(_prototypes, cfDamageType);

        _audio.PlayPvs(sound, Transform(target).Coordinates, sound.Params.WithVariation(0.05f));
    }

    private float GetDistanceFeet(EntityUid from, EntityUid to)
    {
        var a = _transform.GetWorldPosition(from);
        var b = _transform.GetWorldPosition(to);
        var tiles = Vector2.Distance(a, b);
        return CFMelee.TilesToFeet(tiles);
    }

    private void RaiseDamaged(EntityUid target, EntityUid source, int amount)
    {
        var ev = new CFDamagedEvent(source, amount);
        RaiseLocalEvent(target, ref ev);
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

    private string ToSs14DamageType(string pf2eType)
    {
        return CFDamageTypes.TryResolve(_prototypes, pf2eType, out var type) ? type.Ss14Type.Id : "Blunt";
    }
}
