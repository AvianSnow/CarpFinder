using Content.Shared.CF.Combat;

namespace Content.Shared.CF.Effects;

/// <summary>Restores hit points with vitality.</summary>
public sealed partial class CFHealEffect : CFEffect
{
    [DataField]
    public int Dice;

    [DataField]
    public int Die = 8;

    [DataField]
    public int Flat;

    [DataField]
    public bool AddAbilityModifier;

    [DataField]
    public int HeightenDice;

    [DataField]
    public int HeightenFlat;

    /// <summary>Added once per level of the user.</summary>
    [DataField]
    public int PerLevel;

    /// <summary>One more die every this many levels of the user, from level 1. 0 means none.</summary>
    [DataField]
    public int DiceEveryLevels;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var system = entities.System<CFEffectSystem>();
        var dice = Dice + HeightenDice * args.HeightenSteps;
        if (DiceEveryLevels > 0)
            dice += ((entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1) - 1) / DiceEveryLevels;

        var flat = Flat + HeightenFlat * args.HeightenSteps + (AddAbilityModifier ? args.AbilityModifier : 0)
                   + PerLevel * (entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1)
                   + system.PotencyBonus(args);
        var amount = system.Roll(dice, system.SpellDie(args, Die), flat);

        // Healer's Blessing: a little more from a healing spell.
        if (entities.HasComponent<Content.Shared.CF.Spells.CFSpellComponent>(args.Source))
            amount += Math.Max(0, entities.System<CFBonusSystem>().Total(target, CFBonusStat.SpellHealing));

        system.Heal(args.User, target, amount);
    }
}

/// <summary>An extra flat check to end one kind of persistent damage on the target, at the DC that good help gives.</summary>
public sealed partial class CFRecoverPersistentEffect : CFEffect
{
    [DataField(required: true)]
    public Robust.Shared.Prototypes.ProtoId<Content.Shared.CF.DamageTypes.CFDamageTypePrototype> DamageType;

    [DataField]
    public int Dc = 10;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFBonusSystem>().TryRecoverPersistent(target, DamageType, Dc);
    }
}
