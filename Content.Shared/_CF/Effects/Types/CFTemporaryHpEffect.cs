using Content.Shared.CF.Combat;

namespace Content.Shared.CF.Effects;

/// <summary>Temporary Hit Points for the target: a fixed amount, plus some per level of the user.</summary>
public sealed partial class CFTemporaryHpEffect : CFEffect
{
    [DataField]
    public int Amount;

    /// <summary>This much more for each of the user's levels.</summary>
    [DataField]
    public int PerLevel;

    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var target = args.Target ?? args.User;
        var level = entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1;
        entities.System<CFTemporaryHpSystem>().Grant(target, Amount + PerLevel * level, DurationSeconds, $"effect:{args.Source}");
    }
}

/// <summary>Lowers the target's frightened value, as Armored Courage does.</summary>
public sealed partial class CFReduceFrightenedEffect : CFEffect
{
    [DataField]
    public int By = 1;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var target = args.Target ?? args.User;
        if (!entities.TryGetComponent(target, out CFConditionComponent? condition) || condition.Frightened <= 0)
            return;

        condition.Frightened = Math.Max(0, condition.Frightened - By);
        entities.Dirty(target, condition);
    }
}
