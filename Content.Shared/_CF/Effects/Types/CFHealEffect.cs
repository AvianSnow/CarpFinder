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

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var system = entities.System<CFEffectSystem>();
        var flat = Flat + HeightenFlat * args.HeightenSteps + (AddAbilityModifier ? args.AbilityModifier : 0);
        var amount = system.Roll(Dice + HeightenDice * args.HeightenSteps, Die, flat);
        system.Heal(args.User, target, amount);
    }
}
