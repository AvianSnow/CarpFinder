using System.Numerics;
using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
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
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Pathfinder 2e Strikes via stock SS14 melee and projectile hits.
/// </summary>
public sealed partial class CFStrikeSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThresholds = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFCombatBridgeSystem _bridge = default!;
    [Dependency] private CFDyingSystem _dying = default!;

    /// <summary>Unconscious / dying creatures are off-guard (−4 AC status, simplified).</summary>
    private const int DownedAcPenalty = 4;

    [SubscribeLocalEvent]
    private void OnCfWeaponHit(Entity<CFMeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (!TryGetMeleeAttacker(args, out var combat, out var abilities))
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        BeginMeleeStrikes(args.User, args.HitEntities, combat, abilities, CFStrikeWeaponStats.FromMelee(weapon.Comp));
    }

    [SubscribeLocalEvent]
    private void OnUnarmedMeleeHit(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (HasComp<CFMeleeWeaponComponent>(weapon))
            return;

        if (!TryGetMeleeAttacker(args, out var combat, out var abilities))
            return;

        if (!IsUnarmedWeapon(args.User, weapon.Owner))
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        BeginMeleeStrikes(args.User, args.HitEntities, combat, abilities, CFStrikeWeaponStats.Unarmed());
    }

    /// <summary>
    /// Projectile fired from a <see cref="CFRangedWeaponComponent"/> — replace SS14 damage with a PF2e Strike.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBeforeProjectileHit(Entity<ProjectileComponent> projectile, ref BeforeProjectileHitEvent args)
    {
        if (projectile.Comp.Weapon is not { } weaponUid
            || !TryComp(weaponUid, out CFRangedWeaponComponent? ranged))
            return;

        var shooter = args.Shooter ?? projectile.Comp.Shooter;
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
        ResolveStrike(
            shooter.Value,
            args.Target,
            combat,
            abilities,
            stats,
            distanceFeet,
            ranged.Propulsive);
    }

    private bool TryGetMeleeAttacker(
        MeleeHitEvent args,
        out CFCombatantComponent combat,
        out CFAbilityScoreComponent abilities)
    {
        combat = default!;
        abilities = default!;

        if (!args.IsHit || args.HitEntities.Count == 0)
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
        CFStrikeWeaponStats stats)
    {
        foreach (var target in targets)
        {
            ResolveStrike(user, target, combat, abilities, stats, distanceFeet: 0f, propulsive: false);
        }
    }

    private void ResolveStrike(
        EntityUid user,
        EntityUid target,
        CFCombatantComponent attackerCombat,
        CFAbilityScoreComponent attackerAbilities,
        CFStrikeWeaponStats weapon,
        float distanceFeet,
        bool propulsive)
    {
        if (!_bridge.TryEnsureCombatTarget(target, out var defender, out var hardness, out var isObject))
        {
            _popup.PopupEntity(Loc.GetString("cf-combat-strike-no-combatant"), user, user);
            return;
        }

        // Objects at 0 HP are already destroyed; creatures at 0 HP are dying / unconscious and can be finished.
        var alreadyDown = !isObject && _dying.IsDown(defender);
        if (isObject && defender.CurrentHp <= 0)
            return;

        if (!isObject && TryComp(target, out MobStateComponent? mobState) && _mobState.IsDead(target, mobState))
            return;

        var weaponTrained = IsWeaponTrained(attackerCombat, weapon.Proficiency);
        var attackBonus = CFMelee.AttackBonus(
            attackerAbilities,
            attackerCombat,
            weapon,
            weaponTrained,
            distanceFeet);

        // Beyond sixth range increment: automatic miss.
        if (weapon.Ranged && attackBonus <= -900)
        {
            RaiseNetworkEvent(
                new CFCombatNumberEvent(GetNetEntity(target), CFStrikeDegree.Failure, 0, weapon.DamageType),
                Filter.Pvs(target));
            return;
        }

        var targetAc = defender.ArmorClass - (alreadyDown ? DownedAcPenalty : 0);
        var damageMod = CFMelee.DamageBonus(attackerAbilities, weapon, propulsive);
        var d20 = _random.Next(1, 21);
        var degree = CFMelee.DegreeOfSuccess(d20, attackBonus, targetAc);
        var damage = 0;
        var criticalHit = degree == CFStrikeDegree.CriticalSuccess;

        if (degree is CFStrikeDegree.Success or CFStrikeDegree.CriticalSuccess)
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
                    weapon,
                    damageMod,
                    criticalHit);

                if (isObject && hardness > 0)
                    damage = Math.Max(0, damage - hardness);

                if (damage > 0)
                {
                    defender.CurrentHp = Math.Max(0, defender.CurrentHp - damage);
                    Dirty(target, defender);
                    MirrorToSs14(user, target, defender, damage, weapon.DamageType, isObject);
                }

                if (!isObject && defender.CurrentHp <= 0)
                {
                    _popup.PopupEntity(Loc.GetString("cf-combat-strike-defeated"), target, user);
                    _dying.KnockOut(target, defender, criticalHit, user);
                }
                else if (isObject && defender.CurrentHp <= 0)
                {
                    _popup.PopupEntity(Loc.GetString("cf-combat-strike-defeated"), target, user);
                    DestroyObject(user, target);
                }
            }
        }

        RaiseNetworkEvent(
            new CFCombatNumberEvent(GetNetEntity(target), degree, damage, weapon.DamageType),
            Filter.Pvs(target));
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
        var ss14Type = ToSs14DamageType(damageType);
        FixedPoint2 amount;

        // Scale toward Critical (not Dead) so MobThresholds won't finish the kill — CF dying owns death.
        if (!isObject
            && _mobThresholds.TryGetThresholdForState(target, MobState.Critical, out var critThreshold)
            && critThreshold != null
            && defender.MaxHp > 0)
        {
            amount = critThreshold.Value * cfDamage / defender.MaxHp;
            // Leave a sliver below Dead if a Dead threshold exists and equals Crit somehow.
            if (_mobThresholds.TryGetThresholdForState(target, MobState.Dead, out var deadThreshold)
                && deadThreshold != null
                && TryComp(target, out DamageableComponent? damageable))
            {
                var total = _damageable.GetTotalDamage((target, damageable));
                var maxAdd = deadThreshold.Value - FixedPoint2.Epsilon - total;
                if (maxAdd < amount)
                    amount = FixedPoint2.Max(0, maxAdd);
            }
        }
        else
        {
            amount = cfDamage;
        }

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
