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

    /// <summary>Half the user's level more, rounded down (a Witch's Restored Spirit).</summary>
    [DataField]
    public bool HalfLevel;

    [DataField]
    public float DurationSeconds = 60f;

    /// <summary>Added to <see cref="Amount"/> for each rank the spell is heightened.</summary>
    [DataField]
    public int HeightenAmount;

    /// <summary>The user gets them, and the amount also counts the target's level once (Absorb Strength).</summary>
    [DataField]
    public bool FromTargetLevel;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var target = args.Target ?? args.User;
        var level = entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1;
        var amount = Amount + HeightenAmount * args.HeightenSteps + PerLevel * level + (HalfLevel ? level / 2 : 0);
        if (FromTargetLevel)
        {
            amount += entities.GetComponentOrNull<CFCombatantComponent>(target)?.Level ?? 0;
            target = args.User;
        }

        entities.System<CFTemporaryHpSystem>().Grant(target, amount, DurationSeconds, $"effect:{args.Source}");
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

/// <summary>The user regains Focus Points, up to its pool (Energized Font).</summary>
public sealed partial class CFRegainFocusEffect : CFEffect
{
    [DataField]
    public int Points = 1;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (!entities.TryGetComponent(args.User, out Content.Shared.CF.Character.CFSpellbookComponent? book))
            return;

        book.FocusSpent = Math.Max(0, book.FocusSpent - Points);
        entities.Dirty(args.User, book);
    }
}
