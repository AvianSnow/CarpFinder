using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Pathfinder 2e Strikes via stock SS14 light attacks (held CF weapons and unarmed body/gloves).
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
    [Dependency] private CFCombatBridgeSystem _bridge = default!;

    [SubscribeLocalEvent]
    private void OnCfWeaponHit(Entity<CFMeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (!TryGetAttacker(args, out var combat, out var abilities))
            return;

        BeginStrike(args, combat, abilities, weapon.Comp);
    }

    /// <summary>
    /// Empty-hand / glove melee on CF combatants — stock body <see cref="MeleeWeaponComponent"/> without a CF weapon.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnUnarmedMeleeHit(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (HasComp<CFMeleeWeaponComponent>(weapon))
            return;

        if (!TryGetAttacker(args, out var combat, out var abilities))
            return;

        if (!IsUnarmedWeapon(args.User, weapon.Owner))
            return;

        var unarmed = new CFMeleeWeaponComponent
        {
            DamageDice = CFMelee.UnarmedDamageDice,
            DamageDie = CFMelee.UnarmedDamageDie,
            DamageType = CFMelee.UnarmedDamageType,
            Proficiency = CFMelee.UnarmedProficiency,
            Finesse = false,
            ItemBonus = 0,
        };

        BeginStrike(args, combat, abilities, unarmed);
    }

    private bool TryGetAttacker(
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

    /// <summary>
    /// True when the melee "weapon" is the body itself or equipped gloves (not a held item).
    /// </summary>
    private bool IsUnarmedWeapon(EntityUid user, EntityUid weapon)
    {
        if (weapon == user)
            return true;

        if (_inventory.TryGetSlotEntity(user, "gloves", out var gloves) && gloves == weapon)
            return true;

        // Held item without CFMeleeWeapon stays on stock SS14 damage for now.
        if (_hands.IsHolding(user, weapon))
            return false;

        return false;
    }

    private void BeginStrike(
        MeleeHitEvent args,
        CFCombatantComponent attackerCombat,
        CFAbilityScoreComponent attackerAbilities,
        CFMeleeWeaponComponent weapon)
    {
        // Cancel stock damage; we resolve PF2e Strikes ourselves.
        args.Handled = true;

        if (!_net.IsServer)
            return;

        var weaponTrained = IsWeaponTrained(attackerCombat, weapon.Proficiency);
        var attackBonus = CFMelee.AttackBonusFromCombatant(
            attackerAbilities,
            attackerCombat,
            weapon,
            weaponTrained);
        var damageMod = CFMelee.DamageModifier(
            attackerAbilities.GetModifier(CFAbility.Strength),
            weapon.Finesse,
            attackerAbilities.GetModifier(CFAbility.Dexterity));

        foreach (var target in args.HitEntities)
        {
            ResolveStrike(args.User, target, weapon, attackBonus, damageMod);
        }
    }

    private bool IsWeaponTrained(CFCombatantComponent attackerCombat, string proficiency)
    {
        if (attackerCombat.ClassId != null
            && _prototypes.TryIndex(attackerCombat.ClassId, out CFClassPrototype? cls))
        {
            return CFAttributeMath.IsWeaponTrained(cls, proficiency);
        }

        // No class (or commoner) — treat as trained so testing / NPCs still work.
        return true;
    }

    private void ResolveStrike(
        EntityUid user,
        EntityUid target,
        CFMeleeWeaponComponent weapon,
        int attackBonus,
        int damageMod)
    {
        if (!_bridge.TryEnsureCombatTarget(target, out var defender, out var hardness, out var isObject))
        {
            _popup.PopupEntity(Loc.GetString("cf-combat-strike-no-combatant"), user, user);
            return;
        }

        if (defender.CurrentHp <= 0)
            return;

        var d20 = _random.Next(1, 21);
        var degree = CFMelee.DegreeOfSuccess(d20, attackBonus, defender.ArmorClass);
        var damage = 0;

        if (degree is CFStrikeDegree.Success or CFStrikeDegree.CriticalSuccess)
        {
            damage = CFMelee.RollDamage(
                _random,
                weapon.DamageDice,
                weapon.DamageDie,
                damageMod,
                degree == CFStrikeDegree.CriticalSuccess);

            if (isObject && hardness > 0)
                damage = Math.Max(0, damage - hardness);

            if (damage > 0)
            {
                defender.CurrentHp = Math.Max(0, defender.CurrentHp - damage);
                Dirty(target, defender);
                MirrorToSs14(user, target, defender, damage, weapon.DamageType, isObject);
            }
        }

        RaiseNetworkEvent(
            new CFCombatNumberEvent(GetNetEntity(target), degree, damage, weapon.DamageType),
            Filter.Pvs(target));

        if (defender.CurrentHp > 0 || degree is CFStrikeDegree.Failure or CFStrikeDegree.CriticalFailure)
            return;

        _popup.PopupEntity(Loc.GetString("cf-combat-strike-defeated"), target, user);
        DefeatTarget(user, target, isObject);
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

        if (!isObject
            && _mobThresholds.TryGetThresholdForState(target, MobState.Dead, out var deadThreshold)
            && deadThreshold != null
            && defender.MaxHp > 0)
        {
            amount = deadThreshold.Value * cfDamage / defender.MaxHp;
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

    private void DefeatTarget(EntityUid user, EntityUid target, bool isObject)
    {
        if (isObject)
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

            return;
        }

        if (TryComp(target, out MobStateComponent? mobState))
            _mobState.ChangeMobState(target, MobState.Dead, mobState, user);
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
