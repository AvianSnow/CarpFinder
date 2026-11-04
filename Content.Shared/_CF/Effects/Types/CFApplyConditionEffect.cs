using Content.Shared.CF.Combat;

namespace Content.Shared.CF.Effects;

/// <summary>Gives the target a condition through <see cref="CFConditionSystem.ApplyCondition"/>.</summary>
public sealed partial class CFApplyConditionEffect : CFEffect
{
    [DataField(required: true)]
    public CFConditionKind Condition;

    /// <summary>Frightened value, or the shield spell's hardness. Ignored by on/off conditions.</summary>
    [DataField]
    public int Value = 1;

    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        entities.System<CFConditionSystem>()
            .ApplyCondition(target, Condition, Value, DurationSeconds, args.User, args.Dc);
    }
}
