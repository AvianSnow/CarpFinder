using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Traits;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>One bonus or penalty an oracular curse gives.</summary>
[DataDefinition]
public sealed partial class CFCurseBonus
{
    [DataField(required: true)]
    public CFBonusStat Stat;

    [DataField]
    public CFBonusType Type = CFBonusType.Status;

    /// <summary>Negative for a penalty. In <see cref="CFCurseboundComponent.PerValue"/> it is multiplied by the cursebound value.</summary>
    [DataField(required: true)]
    public int Value;

    /// <summary>Only against a source with this trait, as Battle's penalty against spells.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;
}

/// <summary>
/// An Oracle's curse and how far it has taken hold. The cursebound value goes up by 1 each time a cursebound ability is
/// used, to <see cref="Max"/>, and what the curse does grows with it. Refocusing lowers it by 1 and a long rest ends it.
/// The fields below are the curse itself, set by <c>CFGrantCurse</c> from the mystery.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFCurseboundComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Value;

    /// <summary>2 until level 11.</summary>
    [DataField, AutoNetworkedField]
    public int Max = 2;

    /// <summary>Applied once per point of cursebound.</summary>
    [DataField]
    public List<CFCurseBonus> PerValue = new();

    /// <summary>Applied from cursebound 2.</summary>
    [DataField]
    public List<CFCurseBonus> AtTwo = new();

    /// <summary>Conditions the Oracle has at its cursebound value (clumsy for Ancestors, enfeebled for Cosmos).</summary>
    [DataField]
    public List<CFConditionKind> Conditions = new();

    /// <summary>Weakness 2 to these while cursebound.</summary>
    [DataField]
    public List<ProtoId<CFDamageTypePrototype>> Weaknesses = new();

    /// <summary>Persistent damage of this type equal to the cursebound value, each time it rises (Flames).</summary>
    [DataField]
    public ProtoId<CFDamageTypePrototype>? Persistent;

    /// <summary>Healing spells restore this much less per level, per point of cursebound (Life).</summary>
    [DataField]
    public bool LessHealing;

    /// <summary>The last spell cast that was not a cantrip: its rank, its damage type and when (Foretell Harm).</summary>
    [DataField]
    public int LastSpellRank;

    [DataField]
    public string LastSpellDamage = string.Empty;

    [DataField]
    public TimeSpan LastSpellAt;
}

/// <summary>Raises and lowers an Oracle's cursebound value and applies what its curse does at each value.</summary>
public sealed partial class CFCurseboundSystem : EntitySystem
{
    private const string BonusSource = "cursebound";

    /// <summary>The curse lasts until it is lifted, so its timed parts are given this long.</summary>
    private const float Lasting = 86400f;

    /// <summary>How long after a spell Foretell Harm can still follow it.</summary>
    public static readonly TimeSpan SpellLingers = TimeSpan.FromSeconds(6);

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public int Value(EntityUid uid)
    {
        return CompOrNull<CFCurseboundComponent>(uid)?.Value ?? 0;
    }

    /// <summary>Whether the creature can use a cursebound ability: it has a curse, and is not at its most.</summary>
    public bool CanRaise(EntityUid uid)
    {
        return TryComp(uid, out CFCurseboundComponent? curse) && curse.Value < curse.Max;
    }

    /// <summary>A cursebound ability was used: the value goes up by 1.</summary>
    public bool Raise(EntityUid uid)
    {
        if (!_net.IsServer || !TryComp(uid, out CFCurseboundComponent? curse) || curse.Value >= curse.Max)
            return false;

        Set(uid, curse, curse.Value + 1);
        if (curse.Persistent is { } type)
            _bonuses.AddPersistent(uid, type, 0, 0, curse.Value, uid);

        return true;
    }

    /// <summary>Refocusing eases the curse by 1.</summary>
    public void Lower(EntityUid uid)
    {
        if (_net.IsServer && TryComp(uid, out CFCurseboundComponent? curse) && curse.Value > 0)
            Set(uid, curse, curse.Value - 1);
    }

    /// <summary>A long rest ends the curse's hold.</summary>
    public void Clear(EntityUid uid)
    {
        if (_net.IsServer && TryComp(uid, out CFCurseboundComponent? curse) && curse.Value > 0)
            Set(uid, curse, 0);
    }

    private void Set(EntityUid uid, CFCurseboundComponent curse, int value)
    {
        curse.Value = value;
        Dirty(uid, curse);

        _bonuses.RemoveBonusesFrom(uid, BonusSource);
        foreach (var bonus in curse.PerValue)
        {
            _bonuses.AddPermanentBonus(uid, bonus.Stat, bonus.Type, bonus.Value * value, bonus.Trait, source: BonusSource);
        }

        if (value >= 2)
        {
            foreach (var bonus in curse.AtTwo)
            {
                _bonuses.AddPermanentBonus(uid, bonus.Stat, bonus.Type, bonus.Value, bonus.Trait, source: BonusSource);
            }
        }

        if (curse.LessHealing && value > 0)
        {
            var level = Math.Max(1, CompOrNull<CFCombatantComponent>(uid)?.Level ?? 1);
            _bonuses.AddPermanentBonus(uid, CFBonusStat.SpellHealing, CFBonusType.Status, -level * value, source: BonusSource);
        }

        foreach (var condition in curse.Conditions)
        {
            _conditions.EndCondition(uid, condition);
            if (value > 0)
                _conditions.ApplyCondition(uid, condition, value, Lasting, uid, 0);
        }

        foreach (var weakness in curse.Weaknesses)
        {
            _bonuses.EndTimedWeakness(uid, weakness);
            if (value > 0)
                _bonuses.AddResistance(uid, weakness, 2, weakness: true, Lasting);
        }

        _popup.PopupEntity(Loc.GetString(value > 0 ? "cf-oracle-cursebound" : "cf-oracle-cursebound-gone", ("value", value)), uid, uid);
    }

    /// <summary>A spell that is not a cantrip has been cast: Foretell Harm can follow it.</summary>
    public void NoteSpell(EntityUid caster, Entity<CFSpellComponent> spell)
    {
        if (!_net.IsServer || !TryComp(caster, out CFCurseboundComponent? curse) || spell.Comp.Rank == 0 && !spell.Comp.Focus)
            return;

        var level = CompOrNull<CFCombatantComponent>(caster)?.Level ?? 1;
        curse.LastSpellRank = spell.Comp.CastRank > 0 ? spell.Comp.CastRank : Content.Shared.CF.Checks.CFCasting.CantripRank(level);
        curse.LastSpellAt = _timing.CurTime;
        curse.LastSpellDamage = string.Empty;
        if (!TryComp(spell, out CFDegreeEffectsComponent? effects))
            return;

        foreach (var effect in effects.Effects)
        {
            if (effect is not CFDamageEffect damage)
                continue;

            curse.LastSpellDamage = damage.DamageType.Id;
            return;
        }
    }

    /// <summary>Whether the creature cast a damaging spell that was not a cantrip in the last 6 seconds.</summary>
    public bool DamagingSpellRecent(EntityUid caster)
    {
        return TryComp(caster, out CFCurseboundComponent? curse)
               && curse.LastSpellDamage.Length > 0
               && _timing.CurTime - curse.LastSpellAt <= SpellLingers;
    }
}

/// <summary>Foretell Harm: damage of the last spell's type, twice its rank.</summary>
public sealed partial class CFForetellHarmEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target
            || !entities.TryGetComponent(args.User, out CFCurseboundComponent? curse)
            || curse.LastSpellDamage.Length == 0)
        {
            return;
        }

        entities.System<CFEffectSystem>().DealDamage(args.User, target, 2 * Math.Max(1, curse.LastSpellRank), curse.LastSpellDamage, critical: false);
    }
}

/// <summary>Weapon Trance: for a while, martial weapons use the creature's rank with simple weapons.</summary>
public sealed partial class CFWeaponTranceEffect : CFEffect
{
    [DataField]
    public float DurationSeconds = 60f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (entities.TryGetComponent(args.User, out CFCombatantComponent? combat))
            combat.WeaponTranceUntil = IoCManager.Resolve<IGameTiming>().CurTime + TimeSpan.FromSeconds(DurationSeconds);
    }
}
