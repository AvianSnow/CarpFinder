using Content.Shared.CF.Combat;

namespace Content.Shared.CF.Effects;

/// <summary>Leaves the target off-guard to everyone for a while.</summary>
public sealed partial class CFOffGuardEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFConditionSystem>().SetOffGuardAll(target, DurationSeconds);
    }
}
