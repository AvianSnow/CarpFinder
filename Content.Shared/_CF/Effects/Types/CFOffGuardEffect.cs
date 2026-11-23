using Content.Shared.CF.Combat;

namespace Content.Shared.CF.Effects;

/// <summary>Leaves the target off-guard to everyone for a while.</summary>
public sealed partial class CFOffGuardEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 6f;

    /// <summary>Off-guard to the user only, not to everyone (Pointed Question).</summary>
    [DataField]
    public bool ToUser;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        if (ToUser)
            entities.System<CFConditionSystem>().SetOffGuardTo(target, args.User, DurationSeconds);
        else
            entities.System<CFConditionSystem>().SetOffGuardAll(target, DurationSeconds);
    }
}
