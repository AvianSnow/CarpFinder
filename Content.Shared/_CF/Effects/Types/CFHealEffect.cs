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

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var system = entities.System<CFEffectSystem>();
        var flat = Flat + HeightenFlat * args.HeightenSteps + (AddAbilityModifier ? args.AbilityModifier : 0)
                   + PerLevel * (entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1)
                   + system.PotencyBonus(args);
        var amount = system.Roll(Dice + HeightenDice * args.HeightenSteps, system.SpellDie(args, Die), flat);
        system.Heal(args.User, target, amount);
    }
}
