using Content.Shared.CF.Spells;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Rolls damage on the target. In <see cref="CFDegreeEffectsComponent.Effects"/> it scales itself by the check:
/// a missed attack deals none and a critical hit doubles; a basic save is none, half, full, or double.
/// </summary>
public sealed partial class CFDamageEffect : CFEffect
{
    [DataField]
    public int Dice;

    [DataField]
    public int Die = 6;

    [DataField]
    public int Flat;

    [DataField(required: true)]
    public ProtoId<CFDamageTypePrototype> DamageType = CFDamageTypes.Bludgeoning;

    /// <summary>The damage is of the user's innovation core instead: fire, or what Variable Core chose (Explode).</summary>
    [DataField]
    public bool InnovationCore;

    /// <summary>Only for these modes of the source's <c>CFModeChoice</c>. Empty means every mode (Kinetic Ram's push by action count).</summary>
    [DataField]
    public List<byte> OnlyModes = new();

    /// <summary>Adds the user's spellcasting (or key) ability modifier.</summary>
    [DataField]
    public bool AddAbilityModifier;

    /// <summary>Extra dice per heighten step (<see cref="CFHeightenComponent"/>).</summary>
    [DataField]
    public int HeightenDice;

    [DataField]
    public int HeightenFlat;

    /// <summary>At least one die per level of the user, as Explode.</summary>
    [DataField]
    public bool DiceFromLevel;

    /// <summary>One more die every this many levels of the user, from level 1. 0 means none.</summary>
    [DataField]
    public int DiceEveryLevels;

    /// <summary>One more point of flat damage every this many levels of the user, from level 1. 0 means none.</summary>
    [DataField]
    public int FlatEveryLevels;

    /// <summary>False when this effect sits in a degree list and should always deal its full roll.</summary>
    [DataField]
    public bool ScaleByDegree = true;

    /// <summary>Persistent damage added when any damage is dealt. Doubled with the damage.</summary>
    [DataField]
    public int PersistentDice;

    [DataField]
    public int PersistentDie = 6;

    [DataField]
    public int PersistentFlat;

    /// <summary>Flat persistent damage of this much of the user's level, at least 1: 0.5 for half the level (Oil Fire).</summary>
    [DataField]
    public float PersistentFlatPerLevel;

    /// <summary>Persistent damage type. Defaults to <see cref="DamageType"/>.</summary>
    [DataField]
    public ProtoId<CFDamageTypePrototype>? PersistentType;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target || !args.ModeFits(OnlyModes))
            return;

        var system = entities.System<CFEffectSystem>();
        var damageType = args.DamageTypeOverride ?? (InnovationCore ? entities.System<Content.Shared.CF.Actions.CFInventorSystem>().CoreType(args.User) : DamageType);
        var multiplier = ScaleByDegree ? CFEffectSystem.DamageMultiplier(args) : 1f;
        if (multiplier <= 0f)
        {
            system.ShowMiss(target, damageType);
            return;
        }

        var critical = multiplier >= 2f;
        var amp = system.AmpOf(args);
        var dice = (amp is { AmpDice: > 0 } ? amp.AmpDice : Dice) + (amp is { AmpHeightenDice: > 0 } ? amp.AmpHeightenDice : HeightenDice) * args.HeightenSteps;
        if (DiceFromLevel)
            dice = Math.Max(dice, entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1);

        if (DiceEveryLevels > 0)
            dice += ((entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1) - 1) / DiceEveryLevels;

        var flat = Flat + HeightenFlat * args.HeightenSteps + (AddAbilityModifier ? args.AbilityModifier : 0);
        var userLevel = entities.GetComponentOrNull<CFCombatantComponent>(args.User)?.Level ?? 1;
        if (FlatEveryLevels > 0)
            flat += (userLevel - 1) / FlatEveryLevels;

        // An effect that only adds persistent damage (a flaming rune's critical) deals nothing up front.
        if (dice > 0 || flat > 0)
        {
            var rolled = system.Roll(dice, system.SpellDie(args, amp is { AmpDie: > 0 } ? amp.AmpDie : Die), flat + system.DamageBonus(args.User) + system.PotencyBonus(args) + system.UnleashBonus(args));
            var amount = multiplier < 1f ? (int) (rolled * multiplier) : rolled * (int) multiplier;
            system.DealDamage(args.User, target, amount, damageType, critical);
        }

        var persistentFlat = PersistentFlat;
        if (PersistentFlatPerLevel > 0f)
            persistentFlat += Math.Max(1, (int) (userLevel * PersistentFlatPerLevel));

        if (PersistentDice <= 0 && persistentFlat <= 0)
            return;

        var times = critical ? 2 : 1;
        entities.System<CFBonusSystem>().AddPersistent(
            target,
            PersistentType ?? damageType,
            (PersistentDice + (DiceEveryLevels > 0 ? (userLevel - 1) / DiceEveryLevels : 0)) * times,
            PersistentDie,
            persistentFlat * times,
            args.User);
    }
}

/// <summary>
/// For a moment, the user's melee Strikes deal extra dice of another damage type (Inner Upheaval). It uses the same
/// fields as Divine Infusion, so the newer of the two counts.
/// </summary>
public sealed partial class CFInfuseStrikesEffect : CFEffect
{
    [DataField]
    public int Dice = 1;

    [DataField]
    public int Die = 6;

    /// <summary>One more die for each step the source is heightened.</summary>
    [DataField]
    public int HeightenDice;

    [DataField(required: true)]
    public ProtoId<CFDamageTypePrototype> DamageType;

    [DataField]
    public float DurationSeconds = 3f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        var options = entities.EnsureComponent<Content.Shared.CF.Combat.CFCombatOptionsComponent>(args.User);
        options.InfusionDice = Dice + HeightenDice * args.HeightenSteps;
        options.InfusionDie = Die;
        options.InfusionType = DamageType.Id;
        options.InfusionUntil = IoCManager.Resolve<Robust.Shared.Timing.IGameTiming>().CurTime + TimeSpan.FromSeconds(DurationSeconds);
        entities.Dirty(args.User, options);
    }
}
