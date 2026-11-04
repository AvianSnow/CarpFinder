using Content.Shared.CF.Character;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Worn armor: Armor Class, armor runes, the check penalty, and the Speed penalty (with encumbrance).
/// Replaces CP14's <c>ClothingSpeedModifier</c> for CarpFinder armor.
/// </summary>
public sealed partial class CFArmorSystem : EntitySystem
{
    /// <summary>Slowest a creature moves, in feet, however many penalties it has.</summary>
    public const int MinimumSpeedFeet = 5;

    public const int EncumberedSpeedPenalty = -10;

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFEncumbranceSystem _encumbrance = default!;

    /// <summary>The armor worn in an inventory slot. Held armor does not count. The highest AC bonus wins.</summary>
    public bool TryGetWornArmor(EntityUid mob, out Entity<CFArmorComponent> armor)
    {
        armor = default;
        if (!HasComp<InventoryComponent>(mob))
            return false;

        var found = false;
        var best = int.MinValue;
        var slots = _inventory.GetSlotEnumerator(mob);
        while (slots.NextItem(out var item))
        {
            if (!TryComp(item, out CFArmorComponent? comp) || comp.AcBonus <= best)
                continue;

            armor = (item, comp);
            best = comp.AcBonus;
            found = true;
        }

        return found;
    }

    /// <summary>True when the wearer's Strength is at least the armor's Strength value.</summary>
    public bool MeetsStrength(EntityUid mob, CFArmorComponent armor)
    {
        if (armor.Strength is not { } needed)
            return true;

        return TryComp(mob, out CFAbilityScoreComponent? abilities) && abilities.GetModifier(CFAbility.Strength) >= needed;
    }

    /// <summary>
    /// Armor check penalty for one skill check. Applies to Strength- and Dexterity-based checks that do not have
    /// the attack trait, unless Strength meets the armor's value. Flexible armor spares Acrobatics and Athletics.
    /// Noisy armor always applies to Stealth.
    /// </summary>
    public int CheckPenalty(EntityUid mob, string skill, CFAbility ability, bool attackTrait)
    {
        if (attackTrait || ability is not (CFAbility.Strength or CFAbility.Dexterity))
            return 0;

        if (!TryGetWornArmor(mob, out var armor) || armor.Comp.CheckPenalty >= 0)
            return 0;

        var traits = CompOrNull<CFTraitComponent>(armor);
        if (skill == "Stealth" && HasRule(traits, CFTraitRule.Noisy))
            return armor.Comp.CheckPenalty;

        if (MeetsStrength(mob, armor.Comp))
            return 0;

        if (skill is "Acrobatics" or "Athletics" && HasRule(traits, CFTraitRule.Flexible))
            return 0;

        return armor.Comp.CheckPenalty;
    }

    /// <summary>Speed penalty in feet (negative): armor (5 less when Strength is met) plus encumbrance.</summary>
    public int SpeedPenalty(EntityUid mob)
    {
        var penalty = 0;
        if (TryGetWornArmor(mob, out var armor) && armor.Comp.SpeedPenalty < 0)
        {
            penalty = armor.Comp.SpeedPenalty;
            if (MeetsStrength(mob, armor.Comp))
                penalty = Math.Min(0, penalty + 5);
        }

        if (_encumbrance.IsEncumbered(mob))
            penalty += EncumberedSpeedPenalty;

        return penalty;
    }

    /// <summary>
    /// Recomputes a character's AC from worn armor (10 + capped Dex + proficiency + armor bonus) and applies
    /// armor potency and resilient runes as item bonuses. Creatures with a printed AC
    /// (<see cref="CFCombatantComponent.ArmorFromEquipment"/> false) are left alone.
    /// </summary>
    public void RefreshArmorClass(EntityUid mob)
    {
        if (!TryComp(mob, out CFCombatantComponent? combat) || !TryComp(mob, out CFAbilityScoreComponent? abilities))
            return;

        if (!combat.ArmorFromEquipment)
            return;

        CFClassPrototype? cls = null;
        if (combat.ClassId != null)
            _prototypes.TryIndex(combat.ClassId, out cls);

        var acBonus = 0;
        int? dexCap = null;
        var proficiency = "Unarmored";
        EntityUid? worn = null;
        if (TryGetWornArmor(mob, out var armor))
        {
            acBonus = armor.Comp.AcBonus;
            dexCap = armor.Comp.DexCap;
            proficiency = armor.Comp.Proficiency;
            worn = armor.Owner;
        }

        var trained = proficiency == "Unarmored"
            ? CFAttributeMath.IsUnarmoredTrained(cls)
            : CFAttributeMath.IsArmorTrained(cls, proficiency);
        combat.ArmorClass = CFAttributeMath.ComputeAc(abilities.AbilityModifiers, combat.Level, trained, acBonus, dexCap);
        Dirty(mob, combat);

        var runes = worn != null ? CompOrNull<CFArmorRunesComponent>(worn.Value) : null;
        _bonuses.SetEquipmentBonus(mob, CFEquipmentBonusSlot.ArmorPotency, CFBonusStat.ArmorClass, runes?.Potency ?? 0);
        _bonuses.SetEquipmentBonus(mob, CFEquipmentBonusSlot.ArmorResilient, CFBonusStat.Saves, runes?.Resilient ?? 0);
    }

    [SubscribeLocalEvent]
    private void OnEquipped(EntityUid uid, CFArmorComponent comp, GotEquippedEvent args)
    {
        Refresh(args.EquipTarget);
    }

    [SubscribeLocalEvent]
    private void OnUnequipped(EntityUid uid, CFArmorComponent comp, GotUnequippedEvent args)
    {
        Refresh(args.EquipTarget);
    }

    private void Refresh(EntityUid mob)
    {
        RefreshArmorClass(mob);
        _speed.RefreshMovementSpeedModifiers(mob);
    }

    [SubscribeLocalEvent]
    private void OnRefreshSpeed(Entity<CFCombatantComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var penalty = SpeedPenalty(ent);
        if (penalty >= 0)
            return;

        var speed = Math.Max(MinimumSpeedFeet, ent.Comp.SpeedFeet);
        var scale = Math.Max(MinimumSpeedFeet, speed + penalty) / (float) speed;
        args.ModifySpeed(scale, scale);
    }

    private bool HasRule(CFTraitComponent? traits, CFTraitRule rule)
    {
        if (traits == null)
            return false;

        foreach (var id in traits.Traits)
        {
            if (_prototypes.TryIndex(id, out var trait) && trait.Rule == rule)
                return true;
        }

        return false;
    }
}
