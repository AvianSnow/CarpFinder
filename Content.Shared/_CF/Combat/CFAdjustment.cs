using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Shared.CF.Combat;

public enum CFAdjustmentKind : byte
{
    None,
    Elite,
    Weak,
}

/// <summary>
/// Monster Core's elite and weak adjustments, applied once when the creature appears: 2 more or less on AC, attack
/// rolls, damage, saves, Perception and skills, a level up or down, and Hit Points by its level. Set it on a
/// monster, or on a <c>CFEncounterSpawner</c> for everything it spawns.
/// </summary>
[RegisterComponent]
public sealed partial class CFAdjustmentComponent : Component
{
    [DataField(required: true)]
    public CFAdjustmentKind Kind;

    [DataField]
    public bool Applied;
}

public sealed partial class CFAdjustmentSystem : EntitySystem
{
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFAdjustmentComponent> ent, ref MapInitEvent args)
    {
        Apply(ent);
    }

    /// <summary>Makes a creature elite or weak. A creature is only ever adjusted once.</summary>
    public void Adjust(EntityUid uid, CFAdjustmentKind kind)
    {
        if (kind == CFAdjustmentKind.None || HasComp<CFAdjustmentComponent>(uid))
            return;

        var comp = AddComp<CFAdjustmentComponent>(uid);
        comp.Kind = kind;
        Apply((uid, comp));
    }

    private void Apply(Entity<CFAdjustmentComponent> ent)
    {
        if (ent.Comp.Applied || ent.Comp.Kind == CFAdjustmentKind.None || !TryComp(ent, out CFCombatantComponent? combat))
            return;

        ent.Comp.Applied = true;
        var elite = ent.Comp.Kind == CFAdjustmentKind.Elite;
        var step = elite ? 2 : -2;
        var hp = HitPoints(combat.Level, elite);

        combat.ArmorClass += step;
        combat.StrikeAdjustment += step;
        if (combat.PrintedAttack is { } attack)
            combat.PrintedAttack = attack + step;
        if (combat.PrintedDamage is { } damage)
            combat.PrintedDamage = damage + step;
        if (combat.PrintedFortitude is { } fortitude)
            combat.PrintedFortitude = fortitude + step;
        if (combat.PrintedReflex is { } reflex)
            combat.PrintedReflex = reflex + step;
        if (combat.PrintedWill is { } will)
            combat.PrintedWill = will + step;
        if (combat.PrintedPerception is { } perception)
            combat.PrintedPerception = perception + step;

        foreach (var skill in new List<string>(combat.PrintedSkills.Keys))
        {
            combat.PrintedSkills[skill] += step;
        }

        if (TryComp(ent, out CFMonsterAttacksComponent? attacks))
        {
            foreach (var each in attacks.Attacks)
            {
                each.Attack += step;
                each.Bonus += step;
            }

            Dirty(ent, attacks);
        }

        combat.Level += elite ? 1 : -1;
        combat.MaxHp = Math.Max(1, combat.MaxHp + hp);
        combat.CurrentHp = combat.MaxHp;
        Dirty(ent, combat);

        // The stock bar mirrors CF Hit Points, so its thresholds move with them.
        if (HasComp<MobThresholdsComponent>(ent))
        {
            _thresholds.SetMobStateThreshold(ent, FixedPoint2.New(Math.Max(1, combat.MaxHp - 1)), MobState.Critical);
            _thresholds.SetMobStateThreshold(ent, FixedPoint2.New(combat.MaxHp), MobState.Dead);
        }

        var name = MetaData(ent).EntityName;
        _metadata.SetEntityName(ent, Loc.GetString(elite ? "cf-monster-elite" : "cf-monster-weak", ("name", name)));
    }

    /// <summary>The Hit Points an elite gains, or a weak creature loses, by the level it starts at.</summary>
    public static int HitPoints(int level, bool elite)
    {
        if (elite)
            return level <= 1 ? 10 : level <= 4 ? 15 : level <= 19 ? 20 : 30;

        return level <= 2 ? -10 : level <= 5 ? -15 : level <= 20 ? -20 : -30;
    }
}
