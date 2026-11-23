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

    /// <summary>Only when the source's <c>CFModeChoice</c> is in this mode (Cruelty's 2-action Touch of the Void). 0 means always.</summary>
    [DataField]
    public byte OnlyMode;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (OnlyMode != 0 && entities.GetComponentOrNull<CFModeChoiceComponent>(args.Source)?.Chosen != OnlyMode)
            return;

        if (args.Target is not { } target)
            return;

        entities.System<CFCounteractSystem>().Note(args, target, DurationSeconds, Condition);
        entities.System<CFConditionSystem>()
            .ApplyCondition(target, Condition, Value, DurationSeconds, args.User, args.Dc);
    }
}

/// <summary>
/// Mercy: ends the first condition on the target that the user's Mercy covers. The rules make it a counteract check on a
/// condition of the caster's choice.
/// </summary>
public sealed partial class CFMercyEffect : CFEffect
{
    /// <summary>Only when the source's <c>CFModeChoice</c> is in this mode. 0 means always.</summary>
    [DataField]
    public byte OnlyMode;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target
            || OnlyMode != 0 && entities.GetComponentOrNull<CFModeChoiceComponent>(args.Source)?.Chosen != OnlyMode
            || !entities.TryGetComponent(args.User, out Content.Shared.CF.Combat.CFCombatOptionsComponent? options))
        {
            return;
        }

        var conditions = entities.System<Content.Shared.CF.Combat.CFConditionSystem>();
        foreach (var kind in options.MercyConditions)
        {
            if (conditions.Has(target, kind) && conditions.EndCondition(target, kind))
                return;
        }
    }
}

/// <summary>The user gains panache, if it is a Swashbuckler: what a style's own action does on a success (Bon Mot, Dirty Trick).</summary>
public sealed partial class CFGainPanacheEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<Content.Shared.CF.Combat.CFConditionSystem>().GainPanache(args.User);
    }
}

/// <summary>Person of Interest: for a minute, the user's Devise a Stratagem against the target costs no action.</summary>
public sealed partial class CFPersonOfInterestEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var options = entities.EnsureComponent<Content.Shared.CF.Combat.CFCombatOptionsComponent>(args.User);
        options.PersonOfInterest = target;
        options.PersonOfInterestUntil = IoCManager.Resolve<Robust.Shared.Timing.IGameTiming>().CurTime + TimeSpan.FromSeconds(DurationSeconds);
    }
}
