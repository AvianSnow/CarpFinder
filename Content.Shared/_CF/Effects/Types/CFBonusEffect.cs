using Content.Shared.CF.Character;
using Content.Shared.CF.Traits;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>
/// A timed typed bonus or penalty: bless, bane, shield, mystic armor, heroism, enlarge, runic weapon.
/// Same-type bonuses do not stack (<see cref="CFBonusSystem.Total"/>).
/// </summary>
public sealed partial class CFBonusEffect : CFEffect
{
    [DataField(required: true)]
    public CFBonusStat Stat;

    [DataField]
    public CFBonusType Type = CFBonusType.Status;

    /// <summary>Negative for a penalty.</summary>
    [DataField(required: true)]
    public int Value;

    [DataField]
    public float DurationSeconds = 60f;

    /// <summary>When set, the bonus only counts against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>When set with <see cref="CFBonusStat.SkillChecks"/>, the bonus only counts for this skill.</summary>
    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? Skill;

    /// <summary>Locale id shown over the target.</summary>
    [DataField]
    public string? Popup;

    /// <summary>Added to <see cref="Value"/> for each rank the spell is heightened (Healer's Blessing).</summary>
    [DataField]
    public int HeightenValue;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var value = Value + HeightenValue * args.HeightenSteps;
        var source = entities.System<CFCounteractSystem>().Note(args, target, DurationSeconds, bonus: true);
        entities.System<CFBonusSystem>().AddBonus(target, Stat, Type, value, DurationSeconds, Trait, Skill?.Id, source);
        if (Popup != null)
            entities.System<SharedPopupSystem>().PopupEntity(Loc.GetString(Popup), target);
    }
}

/// <summary>The target's next attack roll is rolled twice and uses the higher result (sure strike), or its next save (Bit of Luck).</summary>
public sealed partial class CFFortuneEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 6f;

    /// <summary>The next saving throw instead of the next attack roll.</summary>
    [DataField]
    public bool Save;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        if (Save)
            entities.System<CFBonusSystem>().GrantSaveFortune(target, DurationSeconds);
        else
            entities.System<CFBonusSystem>().GrantFortune(target, DurationSeconds);
    }
}

/// <summary>Quickened (4 actions) or slowed (2 actions) for a time.</summary>
public sealed partial class CFActionCapEffect : CFEffect
{
    [DataField(required: true)]
    public int Cap = 3;

    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        entities.System<CFBonusSystem>().SetActionCap(target, Cap, DurationSeconds);
        entities.System<SharedPopupSystem>().PopupEntity(
            Loc.GetString(Cap > 3 ? "cf-cond-quickened" : "cf-cond-slowed"),
            target);
    }
}
