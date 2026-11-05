using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Hit point changes that effects share: damage with resistances and object hardness, healing, and the floating number.
/// </summary>
public sealed partial class CFEffectSystem
{
    /// <summary>Rolls <paramref name="dice"/>d<paramref name="die"/> + flat. A critical doubles the dice.</summary>
    public static int Roll(IRobustRandom random, int dice, int die, int flat, bool critical)
    {
        if (dice <= 0 || die <= 0)
            return Math.Max(0, flat);

        return CFMelee.RollDamage(random, dice, die, flat, critical);
    }

    /// <summary>What the user adds to a damage roll that is not a Strike.</summary>
    public int DamageBonus(EntityUid user)
    {
        return _bonuses.Total(user, CFBonusStat.Damage);
    }

    public int Roll(int dice, int die, int flat)
    {
        return Roll(_random, dice, die, flat, critical: false);
    }

    /// <summary>
    /// How much of a damage roll a target takes for the check's degree.
    /// Attacks: miss none, hit full, critical hit double. Basic saves: none, half, full, double.
    /// No check, or a save that is not basic: full.
    /// </summary>
    public static float DamageMultiplier(CFEffectArgs args)
    {
        switch (args.Defense)
        {
            case CFDefenseType.None:
                return 1f;
            case CFDefenseType.ArmorClass:
                return args.Degree switch
                {
                    CFDegree.CriticalSuccess => 2f,
                    CFDegree.Success => 1f,
                    _ => 0f,
                };
        }

        if (!args.BasicSave)
            return 1f;

        return args.Degree switch
        {
            CFDegree.CriticalSuccess => 0f,
            CFDegree.Success => 0.5f,
            CFDegree.Failure => 1f,
            _ => 2f,
        };
    }

    /// <summary>
    /// Deals damage of a type. Weakness and resistance apply, then an object's hardness.
    /// A creature already at 0 HP gains dying instead.
    /// </summary>
    public void DealDamage(EntityUid user, EntityUid target, int amount, string damageType, bool critical)
    {
        if (!_bridge.TryEnsureCombatTarget(target, out var combat, out var hardness, out var isObject))
            return;

        amount = _bonuses.ModifyDamage(target, damageType, amount);
        if (isObject && hardness > 0)
            amount = Math.Max(0, amount - hardness);

        var display = amount > 0 && critical ? CFDegree.CriticalSuccess : CFDegree.Success;
        if (!isObject && _dying.IsDown(combat))
        {
            _dying.ApplyDamageWhileDown(target, combat, critical, user);
            ShowNumber(target, display, amount, damageType);
            return;
        }

        if (amount > 0)
        {
            combat.CurrentHp = Math.Max(0, combat.CurrentHp - amount);
            Dirty(target, combat);
        }

        if (!isObject && combat.CurrentHp <= 0 && amount > 0)
            _dying.KnockOut(target, combat, critical, user);
        else if (!isObject)
            _dying.SyncHealthBar(target, combat);

        ShowNumber(target, display, amount, damageType);
    }

    public void Heal(EntityUid user, EntityUid target, int amount)
    {
        if (amount <= 0 || !_bridge.TryEnsureCombatTarget(target, out _, out _, out _))
            return;

        _dying.RestoreHitPoints(target, amount, user);
        ShowNumber(target, CFDegree.Success, amount, "Vitality");
    }

    /// <summary>The floating "miss" number for an attack that missed or a save that took nothing.</summary>
    public void ShowMiss(EntityUid target, string damageType)
    {
        ShowNumber(target, CFDegree.Failure, 0, damageType);
    }

    public void ShowNumber(EntityUid target, CFDegree degree, int amount, string damageType)
    {
        RaiseNetworkEvent(
            new CFCombatNumberEvent(GetNetEntity(target), degree, amount, damageType),
            Filter.Pvs(target));
    }
}
