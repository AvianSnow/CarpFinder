using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.Popups;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Tamper: the target's held weapon takes -2 circumstance to attack and damage rolls, or with no weapon its armor hampers it,
/// off-guard and -10 feet. The rules let the Inventor choose: here a held weapon is always what gets tampered with. With
/// <see cref="Backfire"/>, the tools blow up in the user's face instead for fire damage equal to their level.
/// </summary>
public sealed partial class CFTamperEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 6f;

    [DataField]
    public bool Backfire;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var popup = entities.System<SharedPopupSystem>();
        if (Backfire)
        {
            var level = entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1;
            entities.System<CFEffectSystem>().DealDamage(args.User, args.User, level, CFInventorSystem.Fire, critical: false);
            popup.PopupEntity(Loc.GetString("cf-inventor-tamper-backfires"), args.User, args.User);
            return;
        }

        if (args.Target is not { } target || target == args.User)
            return;

        var bonuses = entities.System<CFBonusSystem>();
        if (entities.System<CFInventorSystem>().HeldWeapon(target) != null)
        {
            bonuses.AddBonus(target, CFBonusStat.AttackRoll, CFBonusType.Circumstance, -2, DurationSeconds);
            bonuses.AddBonus(target, CFBonusStat.Damage, CFBonusType.Circumstance, -2, DurationSeconds);
            popup.PopupEntity(Loc.GetString("cf-inventor-tampered-weapon"), target);
            return;
        }

        entities.System<CFConditionSystem>().SetOffGuardAll(target, DurationSeconds);
        bonuses.AddBonus(target, CFBonusStat.Speed, CFBonusType.Circumstance, -10, DurationSeconds);
        popup.PopupEntity(Loc.GetString("cf-inventor-tampered-armor"), target);
    }
}

/// <summary>The target slips out of whatever grab holds it, as Oil Fire's save lets go (the grab and its pull end together).</summary>
public sealed partial class CFReleaseGrabEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFConditionSystem>().ReleaseGrab(target);
    }
}
