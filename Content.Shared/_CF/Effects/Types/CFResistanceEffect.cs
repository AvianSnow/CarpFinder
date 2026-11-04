using Content.Shared.CF.DamageTypes;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>The target resists an amount of one damage type for a time (resist energy).</summary>
public sealed partial class CFResistanceEffect : CFEffect
{
    [DataField(required: true)]
    public ProtoId<CFDamageTypePrototype> DamageType;

    [DataField(required: true)]
    public int Amount;

    [DataField]
    public int HeightenAmount;

    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var amount = Amount + HeightenAmount * args.HeightenSteps;
        entities.System<CFBonusSystem>().AddResistance(target, DamageType, amount, weakness: false, DurationSeconds);
        entities.System<SharedPopupSystem>().PopupEntity(
            Loc.GetString("cf-cond-resist", ("type", DamageType.Id), ("amount", amount)),
            target);
    }
}

/// <summary>The target takes extra damage from one damage type for a time.</summary>
public sealed partial class CFWeaknessEffect : CFEffect
{
    [DataField(required: true)]
    public ProtoId<CFDamageTypePrototype> DamageType;

    [DataField(required: true)]
    public int Amount;

    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        entities.System<CFBonusSystem>().AddResistance(target, DamageType, Amount, weakness: true, DurationSeconds);
        entities.System<SharedPopupSystem>().PopupEntity(
            Loc.GetString("cf-cond-weakness", ("type", DamageType.Id), ("amount", Amount)),
            target);
    }
}
