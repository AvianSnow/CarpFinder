using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Traits;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Effects;

/// <summary>Vitality heals the living and harms the undead. Void does the opposite.</summary>
public enum CFVitalEnergy : byte
{
    Vitality,
    Void,
}

/// <summary>
/// Heal and Harm. The roll heals a creature this energy heals, and damages one it harms, scaled by its basic save. A
/// construct is neither. Put the save on the source's <c>CFCheck</c> with <c>harmedBy</c>, so only those it harms roll.
/// It also runs the Cleric feats that change Heal and Harm: Communal Healing, Panic the Dead, Sap Life and Divine Infusion.
/// </summary>
public sealed partial class CFVitalEnergyEffect : CFEffect
{
    [DataField(required: true)]
    public CFVitalEnergy Energy;

    [DataField]
    public int Dice = 1;

    [DataField]
    public int Die = 8;

    [DataField]
    public int HeightenDice = 1;

    /// <summary>Added to healing only, never to damage (the 2-action version's +8).</summary>
    [DataField]
    public int HealingFlat;

    [DataField]
    public int HeightenHealingFlat;

    /// <summary>With a mode menu, <see cref="HealingFlat"/> only counts in this mode, or with no mode picked. 0 is every mode.</summary>
    [DataField]
    public byte HealingFlatMode;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var system = entities.System<CFEffectSystem>();
        var rolled = system.Roll(Dice + HeightenDice * args.HeightenSteps, system.SpellDie(args, Die), 0);

        if (CFVitalEnergies.HealsFor(entities, args.User, target, Energy))
        {
            var mode = entities.GetComponentOrNull<CFModeChoiceComponent>(args.Source);
            // No mode picked is the ordinary 2-action cast, as a creature with no player makes it.
            if (HealingFlatMode == 0 || mode == null || mode.Chosen == 0 || mode.Chosen == HealingFlatMode)
                rolled += HealingFlat + HeightenHealingFlat * args.HeightenSteps;

            // Sorcerous Potency adds the slot's rank to healing as it does to damage.
            rolled += system.PotencyBonus(args);

            if (entities.HasComponent<CFSpellComponent>(args.Source))
                rolled += Math.Max(0, entities.System<CFBonusSystem>().Total(target, CFBonusStat.SpellHealing));

            // Invert Harm: void heals a living ally, so it is given as the healing that reaches the living.
            system.Heal(args.User, target, rolled, Energy == CFVitalEnergy.Void && !CFVitalEnergies.IsLiving(entities, target));
            if (!system.IsAreaUse(args.Source))
                AfterSingleHeal(entities, args, target);

            return;
        }

        if (!CFVitalEnergies.HarmsFor(entities, args.User, target, Energy))
            return;

        // Mastery of Life and Death: the energy that hurts this one, when the spell's own would not.
        var energy = CFVitalEnergies.Harms(entities, target, Energy) ? Energy : CFVitalEnergies.Other(Energy);
        var multiplier = CFEffectSystem.DamageMultiplier(args);
        if (multiplier <= 0f)
        {
            system.ShowMiss(target, energy.ToString());
            return;
        }

        rolled += system.DamageBonus(args.User) + system.PotencyBonus(args) + system.UnleashBonus(args);
        var amount = multiplier < 1f ? (int) (rolled * multiplier) : rolled * (int) multiplier;
        system.DealDamage(args.User, target, amount, energy.ToString(), multiplier >= 2f);
        entities.System<CFSpellshapeSystem>().NoteDamage(args.User, args.Source, amount);
        AfterHarm(entities, args, target, amount);
    }

    /// <summary>Communal Healing and Divine Infusion, which only follow a heal on one creature.</summary>
    private void AfterSingleHeal(IEntityManager entities, CFEffectArgs args, EntityUid target)
    {
        var options = entities.GetComponentOrNull<CFCombatOptionsComponent>(args.User);
        if (options is { CommunalHealing: true } && Energy == CFVitalEnergy.Vitality)
            entities.System<CFEffectSystem>().CommunalHealing(args.User, target, Math.Max(1, args.Rank));

        if (entities.System<CFSpellshapeSystem>().IsDivineInfusion(args.User)
            && entities.TryGetComponent(target, out CFCombatOptionsComponent? infused))
        {
            infused.InfusionDie = 6;
            infused.InfusionDice = args.Rank >= 8 ? 3 : args.Rank >= 5 ? 2 : 1;
            infused.InfusionType = Energy.ToString();
            infused.InfusionUntil = IoCManager.Resolve<IGameTiming>().CurTime + TimeSpan.FromSeconds(6);
            entities.Dirty(target, infused);
        }
    }

    /// <summary>Panic the Dead and Sap Life.</summary>
    private void AfterHarm(IEntityManager entities, CFEffectArgs args, EntityUid target, int amount)
    {
        var options = entities.GetComponentOrNull<CFCombatOptionsComponent>(args.User);
        if (options == null)
            return;

        // Panic the Dead: an undead that fails its save is frightened 1. Fleeing on a critical failure is TBA.
        if (Energy == CFVitalEnergy.Vitality && options.PanicTheDead && args.Degree is CFDegree.Failure or CFDegree.CriticalFailure)
            entities.System<CFConditionSystem>().ApplyFrightened(target, 1);

        // Sap Life: once per cast, the caster regains the spell's rank in Hit Points when it hurts a living creature.
        if (Energy == CFVitalEnergy.Void && options.SapLife && amount > 0 && CFVitalEnergies.Heals(entities, args.User, CFVitalEnergy.Vitality))
        {
            var tick = IoCManager.Resolve<IGameTiming>().CurTick.Value;
            if (options.SapLifeTick == tick)
                return;

            options.SapLifeTick = tick;
            entities.System<CFEffectSystem>().Heal(args.User, args.User, Math.Max(1, args.Rank));
        }
    }
}

public static class CFVitalEnergies
{
    /// <summary>Not undead and not a construct.</summary>
    public static bool IsLiving(IEntityManager entities, EntityUid uid)
    {
        return !entities.HasComponent<CFVoidHealingComponent>(uid)
               && entities.GetComponentOrNull<CFTraitComponent>(uid)?.HasTrait("Construct") != true;
    }

    public static bool Heals(IEntityManager entities, EntityUid uid, CFVitalEnergy energy)
    {
        return energy == CFVitalEnergy.Vitality ? IsLiving(entities, uid) : entities.HasComponent<CFVoidHealingComponent>(uid);
    }

    public static bool Harms(IEntityManager entities, EntityUid uid, CFVitalEnergy energy)
    {
        return energy == CFVitalEnergy.Vitality ? entities.HasComponent<CFVoidHealingComponent>(uid) : IsLiving(entities, uid);
    }

    public static CFVitalEnergy Other(CFVitalEnergy energy)
    {
        return energy == CFVitalEnergy.Vitality ? CFVitalEnergy.Void : CFVitalEnergy.Vitality;
    }

    /// <summary>
    /// Whether <paramref name="user"/>'s spell of this energy heals <paramref name="target"/>. A Necromancer with Mastery of
    /// Life and Death never heals an enemy, and one with Invert Harm heals living allies with Harm.
    /// </summary>
    public static bool HealsFor(IEntityManager entities, EntityUid user, EntityUid target, CFVitalEnergy energy)
    {
        var options = entities.GetComponentOrNull<CFCombatOptionsComponent>(user);
        var enemy = user != target && entities.System<Content.Shared.CF.Factions.CFFactionSystem>().CanHarm(user, target);
        if (options is { MasteryOfLifeAndDeath: true } && enemy)
            return false;

        if (options is { InvertHarm: true } && energy == CFVitalEnergy.Void && !enemy && IsLiving(entities, target))
            return true;

        return Heals(entities, target, energy);
    }

    /// <summary>Whether <paramref name="user"/>'s spell of this energy hurts <paramref name="target"/>. See <see cref="HealsFor"/>.</summary>
    public static bool HarmsFor(IEntityManager entities, EntityUid user, EntityUid target, CFVitalEnergy energy)
    {
        var options = entities.GetComponentOrNull<CFCombatOptionsComponent>(user);
        var enemy = user != target && entities.System<Content.Shared.CF.Factions.CFFactionSystem>().CanHarm(user, target);
        if (options is { MasteryOfLifeAndDeath: true } && enemy)
            return Harms(entities, target, energy) || Harms(entities, target, Other(energy));

        if (options is { InvertHarm: true } && energy == CFVitalEnergy.Void && !enemy && IsLiving(entities, target))
            return false;

        return Harms(entities, target, energy);
    }
}
