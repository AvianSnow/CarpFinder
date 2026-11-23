using Content.Shared.CF.Combat;

namespace Content.Shared.CF.Effects;

/// <summary>Guiding Shot: the next creature other than the user to attack the target gets this circumstance bonus.</summary>
public sealed partial class CFGuideEffect : CFEffect
{
    [DataField]
    public int Bonus = 1;

    [DataField]
    public float DurationSeconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target && target != args.User)
            entities.System<CFConditionSystem>().SetGuidance(target, args.User, Bonus, DurationSeconds);
    }
}

/// <summary>Protective Screen: for a while the target's spells and ranged attacks set off no reactions.</summary>
public sealed partial class CFScreenEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFConditionSystem>().Screen(target, DurationSeconds);
    }
}

/// <summary>Mountaineering Training: a climb Speed for a while, which skips the check on a ladder.</summary>
public sealed partial class CFClimbSpeedEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var options = entities.EnsureComponent<CFCombatOptionsComponent>(target);
        options.ClimbSpeedUntil = IoCManager.Resolve<Robust.Shared.Timing.IGameTiming>().CurTime + TimeSpan.FromSeconds(DurationSeconds);
        entities.Dirty(target, options);
    }
}
