using Content.Shared.CF.Spells;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Rolls damage on the target. In <see cref="CFDegreeEffectsComponent.Effects"/> it scales itself by the check:
/// a missed attack deals none and a critical hit doubles; a basic save is none, half, full, or double.
/// </summary>
public sealed partial class CFDamageEffect : CFEffect
{
    [DataField]
    public int Dice;

    [DataField]
    public int Die = 6;

    [DataField]
    public int Flat;

    [DataField(required: true)]
    public ProtoId<CFDamageTypePrototype> DamageType = CFDamageTypes.Bludgeoning;

    /// <summary>Adds the user's spellcasting (or key) ability modifier.</summary>
    [DataField]
    public bool AddAbilityModifier;

    /// <summary>Extra dice per heighten step (<see cref="CFHeightenComponent"/>).</summary>
    [DataField]
    public int HeightenDice;

    [DataField]
    public int HeightenFlat;

    /// <summary>At least one die per level of the user, as Explode.</summary>
    [DataField]
    public bool DiceFromLevel;

    /// <summary>One more die every this many levels of the user, from level 1. 0 means none.</summary>
    [DataField]
    public int DiceEveryLevels;

    /// <summary>False when this effect sits in a degree list and should always deal its full roll.</summary>
    [DataField]
    public bool ScaleByDegree = true;

    /// <summary>Persistent damage added when any damage is dealt. Doubled with the damage.</summary>
    [DataField]
    public int PersistentDice;

    [DataField]
    public int PersistentDie = 6;

    [DataField]
    public int PersistentFlat;

    /// <summary>Persistent damage type. Defaults to <see cref="DamageType"/>.</summary>
    [DataField]
    public ProtoId<CFDamageTypePrototype>? PersistentType;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var system = entities.System<CFEffectSystem>();
        var multiplier = ScaleByDegree ? CFEffectSystem.DamageMultiplier(args) : 1f;
        if (multiplier <= 0f)
        {
            system.ShowMiss(target, DamageType);
            return;
        }

        var critical = multiplier >= 2f;
        var amp = system.AmpOf(args);
        var dice = (amp is { AmpDice: > 0 } ? amp.AmpDice : Dice) + (amp is { AmpHeightenDice: > 0 } ? amp.AmpHeightenDice : HeightenDice) * args.HeightenSteps;
        if (DiceFromLevel)
            dice = Math.Max(dice, entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1);

        if (DiceEveryLevels > 0)
            dice += ((entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1) - 1) / DiceEveryLevels;

        var flat = Flat + HeightenFlat * args.HeightenSteps + (AddAbilityModifier ? args.AbilityModifier : 0);

        // An effect that only adds persistent damage (a flaming rune's critical) deals nothing up front.
        if (dice > 0 || flat > 0)
        {
            var rolled = system.Roll(dice, system.SpellDie(args, amp is { AmpDie: > 0 } ? amp.AmpDie : Die), flat + system.DamageBonus(args.User) + system.PotencyBonus(args) + system.UnleashBonus(args));
            var amount = multiplier < 1f ? (int) (rolled * multiplier) : rolled * (int) multiplier;
            system.DealDamage(args.User, target, amount, DamageType, critical);
        }

        if (PersistentDice <= 0 && PersistentFlat <= 0)
            return;

        var times = critical ? 2 : 1;
        entities.System<CFBonusSystem>().AddPersistent(
            target,
            PersistentType ?? DamageType,
            PersistentDice * times,
            PersistentDie,
            PersistentFlat * times,
            args.User);
    }
}
