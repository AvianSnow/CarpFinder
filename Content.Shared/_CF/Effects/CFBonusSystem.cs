using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Traits;
using Content.Shared.Movement.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Typed bonuses, fortune on the next attack, action caps, resistances, weaknesses, and persistent damage.
/// Strikes, spell attacks, saves, and skill checks read their totals here.
/// </summary>
public sealed partial class CFBonusSystem : EntitySystem
{
    /// <summary>One round. Persistent damage ticks this often.</summary>
    public static readonly TimeSpan Round = TimeSpan.FromSeconds(6);

    public const int PersistentFlatCheckDc = 15;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        ExpireBonuses(now);
        ExpireResistances(now);
        TickPersistent(now);
    }

    #region Bonuses

    /// <param name="trait">The bonus only counts against a source with this trait.</param>
    /// <param name="skill">With <see cref="CFBonusStat.SkillChecks"/>, the bonus only counts for this skill.</param>
    public void AddBonus(
        EntityUid uid,
        CFBonusStat stat,
        CFBonusType type,
        int value,
        float seconds,
        ProtoId<CFTraitPrototype>? trait = null,
        string? skill = null)
    {
        if (value == 0)
            return;

        var comp = EnsureComp<CFBonusesComponent>(uid);
        var expires = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, seconds));

        // Recasting the same spell refreshes it instead of piling up entries.
        foreach (var bonus in comp.Bonuses)
        {
            if (bonus.Stat != stat
                || bonus.Type != type
                || bonus.Value != value
                || bonus.Trait != trait
                || bonus.Skill != skill
                || bonus.Expires == TimeSpan.MaxValue)
            {
                continue;
            }

            if (expires > bonus.Expires)
            {
                bonus.Expires = expires;
                Dirty(uid, comp);
            }

            return;
        }

        comp.Bonuses.Add(new CFActiveBonus
        {
            Stat = stat,
            Type = type,
            Value = value,
            Expires = expires,
            Trait = trait,
            Skill = skill,
        });
        Dirty(uid, comp);

        if (stat == CFBonusStat.Speed)
            _speed.RefreshMovementSpeedModifiers(uid);
    }

    /// <summary>
    /// A bonus from a character option that never runs out. It is never merged with another entry, because
    /// two untyped bonuses from two feats both count.
    /// </summary>
    public void AddPermanentBonus(
        EntityUid uid,
        CFBonusStat stat,
        CFBonusType type,
        int value,
        ProtoId<CFTraitPrototype>? trait = null,
        string? skill = null)
    {
        if (value == 0)
            return;

        var comp = EnsureComp<CFBonusesComponent>(uid);
        comp.Bonuses.Add(new CFActiveBonus
        {
            Stat = stat,
            Type = type,
            Value = value,
            Expires = TimeSpan.MaxValue,
            Trait = trait,
            Skill = skill,
        });
        Dirty(uid, comp);
    }

    /// <summary>
    /// A lasting item bonus from worn equipment (armor potency, resilient). 0 removes it.
    /// It obeys the same stacking as timed bonuses, so mystic armor does not stack with an armor potency rune.
    /// </summary>
    public void SetEquipmentBonus(EntityUid uid, CFEquipmentBonusSlot slot, CFBonusStat stat, int value)
    {
        if (value == 0 && !HasComp<CFBonusesComponent>(uid))
            return;

        var comp = EnsureComp<CFBonusesComponent>(uid);
        comp.Bonuses.RemoveAll(b => b.Equipment == slot);
        Dirty(uid, comp);
        if (value == 0)
            return;

        comp.Bonuses.Add(new CFActiveBonus
        {
            Stat = stat,
            Type = CFBonusType.Item,
            Value = value,
            Expires = TimeSpan.MaxValue,
            Equipment = slot,
        });
    }

    /// <summary>The best bonus plus the worst penalty of one type for a stat.</summary>
    public int TotalOfType(EntityUid uid, CFBonusStat stat, CFBonusType type)
    {
        if (!TryComp(uid, out CFBonusesComponent? comp))
            return 0;

        var best = 0;
        var worst = 0;
        foreach (var bonus in comp.Bonuses)
        {
            if (bonus.Stat != stat || bonus.Type != type || bonus.Trait != null || bonus.Skill != null)
                continue;

            if (type == CFBonusType.Untyped)
                best += bonus.Value;
            else if (bonus.Value > 0)
                best = Math.Max(best, bonus.Value);
            else
                worst = Math.Min(worst, bonus.Value);
        }

        return best + worst;
    }

    /// <summary>
    /// The total modifier for a stat. Per type, the highest bonus and the worst penalty apply.
    /// Untyped values all stack. Weapon dice take the highest value.
    /// </summary>
    public int Total(
        EntityUid uid,
        CFBonusStat stat,
        CFBonusStat? also = null,
        HashSet<ProtoId<CFTraitPrototype>>? traits = null,
        string? skill = null)
    {
        if (!TryComp(uid, out CFBonusesComponent? comp) || comp.Bonuses.Count == 0)
            return 0;

        return Total(comp.Bonuses, stat, also, traits, skill);
    }

    /// <summary>The total from spells and other timed sources, leaving out what lasts: feats and equipment.</summary>
    public int TimedTotal(EntityUid uid, CFBonusStat stat, CFBonusStat? also = null)
    {
        return TryComp(uid, out CFBonusesComponent? comp) ? Total(comp.Bonuses, stat, also, timedOnly: true) : 0;
    }

    /// <summary>
    /// <paramref name="also"/> is a narrower stat that applies to this roll too. The two are totalled together,
    /// so a status bonus to one does not stack with a status bonus to the other.
    /// A bonus tied to a trait counts only when <paramref name="traits"/> has it, and one tied to a skill only when
    /// <paramref name="skill"/> is that skill.
    /// </summary>
    public static int Total(
        IReadOnlyList<CFActiveBonus> bonuses,
        CFBonusStat stat,
        CFBonusStat? also = null,
        HashSet<ProtoId<CFTraitPrototype>>? traits = null,
        string? skill = null,
        bool timedOnly = false)
    {
        // Plain locals per type: stackalloc/Span fails the client sandbox's IL verification.
        int status = 0, statusPenalty = 0;
        int circumstance = 0, circumstancePenalty = 0;
        int item = 0, itemPenalty = 0;
        var untyped = 0;
        var weaponDice = 0;
        foreach (var bonus in bonuses)
        {
            if (bonus.Stat != stat && bonus.Stat != also)
                continue;

            if (timedOnly && bonus.Expires == TimeSpan.MaxValue)
                continue;

            if (bonus.Trait is { } trait && traits?.Contains(trait) != true)
                continue;

            if (bonus.Skill != null && bonus.Skill != skill)
                continue;

            if (stat == CFBonusStat.WeaponDice)
            {
                weaponDice = Math.Max(weaponDice, bonus.Value);
                continue;
            }

            switch (bonus.Type)
            {
                case CFBonusType.Status:
                    Keep(ref status, ref statusPenalty, bonus.Value);
                    break;
                case CFBonusType.Circumstance:
                    Keep(ref circumstance, ref circumstancePenalty, bonus.Value);
                    break;
                case CFBonusType.Item:
                    Keep(ref item, ref itemPenalty, bonus.Value);
                    break;
                default:
                    untyped += bonus.Value;
                    break;
            }
        }

        if (stat == CFBonusStat.WeaponDice)
            return weaponDice;

        return untyped + status + statusPenalty + circumstance + circumstancePenalty + item + itemPenalty;
    }

    /// <summary>The highest bonus and the worst penalty of one type.</summary>
    private static void Keep(ref int best, ref int worst, int value)
    {
        if (value > 0)
            best = Math.Max(best, value);
        else
            worst = Math.Min(worst, value);
    }

    /// <summary>From now on this result of a matching save is a degree better.</summary>
    public void AddSaveUpgrade(EntityUid uid, CFDefenseType? save, ProtoId<CFTraitPrototype>? trait, CFDegree from)
    {
        EnsureComp<CFBonusesComponent>(uid).SaveUpgrades.Add(new CFSaveUpgrade { Save = save, Trait = trait, From = from });
    }

    /// <summary>The save's degree after any upgrade. However many match, a result only improves once.</summary>
    public CFDegree UpgradeSave(
        EntityUid uid,
        CFDefenseType defense,
        CFDegree degree,
        HashSet<ProtoId<CFTraitPrototype>>? traits)
    {
        if (!defense.IsSave() || !TryComp(uid, out CFBonusesComponent? comp))
            return degree;

        foreach (var upgrade in comp.SaveUpgrades)
        {
            if (upgrade.From != degree || (upgrade.Save is { } save && save != defense))
                continue;

            if (upgrade.Trait is { } trait && traits?.Contains(trait) != true)
                continue;

            return CFDegrees.Improve(degree);
        }

        return degree;
    }

    public void GrantFortune(EntityUid uid, float seconds)
    {
        var comp = EnsureComp<CFBonusesComponent>(uid);
        var until = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, seconds));
        if (until > comp.FortuneUntil)
            comp.FortuneUntil = until;
    }

    /// <summary>True once per grant: the attack being rolled now is rolled twice.</summary>
    public bool TryConsumeFortune(EntityUid uid)
    {
        if (!TryComp(uid, out CFBonusesComponent? comp) || comp.FortuneUntil <= _timing.CurTime)
            return false;

        comp.FortuneUntil = TimeSpan.Zero;
        return true;
    }

    /// <summary>Quickened (4) or slowed (2) until the time runs out, then back to 3 actions.</summary>
    public void SetActionCap(EntityUid uid, int cap, float seconds)
    {
        var comp = EnsureComp<CFBonusesComponent>(uid);
        comp.ActionCap = cap;
        comp.ActionCapUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, seconds));
        _actions.SetMaxActions(uid, cap);
    }

    private void ExpireBonuses(TimeSpan now)
    {
        var query = EntityQueryEnumerator<CFBonusesComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var speed = Total(comp.Bonuses, CFBonusStat.Speed);
            if (comp.Bonuses.RemoveAll(b => b.Expires <= now) > 0)
            {
                Dirty(uid, comp);
                if (speed != Total(comp.Bonuses, CFBonusStat.Speed))
                    _speed.RefreshMovementSpeedModifiers(uid);
            }

            if (comp.ActionCap != 0 && comp.ActionCapUntil <= now)
            {
                comp.ActionCap = 0;
                _actions.SetMaxActions(uid, 3);
            }
        }
    }

    #endregion

    #region Resistances and weaknesses

    public void AddResistance(EntityUid uid, ProtoId<CFDamageTypePrototype> type, int amount, bool weakness, float seconds)
    {
        if (amount <= 0)
            return;

        var comp = EnsureComp<CFResistancesComponent>(uid);
        var expires = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, seconds));
        foreach (var entry in comp.Entries)
        {
            // A lasting entry stays as it is, or a spell running out would take it away too.
            if (entry.DamageType != type || entry.Weakness != weakness || entry.Expires == TimeSpan.Zero)
                continue;

            entry.Amount = Math.Max(entry.Amount, amount);
            if (expires > entry.Expires)
                entry.Expires = expires;
            return;
        }

        comp.Entries.Add(new CFActiveResistance
        {
            DamageType = type,
            Amount = amount,
            Weakness = weakness,
            Expires = expires,
        });
    }

    /// <summary>A resistance from a character option. <paramref name="halfLevel"/> grows with the creature.</summary>
    public void AddPermanentResistance(EntityUid uid, ProtoId<CFDamageTypePrototype> type, int amount, bool halfLevel)
    {
        if (amount <= 0 && !halfLevel)
            return;

        EnsureComp<CFResistancesComponent>(uid).Entries.Add(new CFActiveResistance
        {
            DamageType = type,
            Amount = amount,
            HalfLevel = halfLevel,
        });
    }

    /// <summary>
    /// Applies the highest weakness and the highest resistance that match the damage type.
    /// Resistance cannot take damage below 0.
    /// </summary>
    public int ModifyDamage(EntityUid target, string damageType, int damage)
    {
        if (damage <= 0 || !TryComp(target, out CFResistancesComponent? comp) || comp.Entries.Count == 0)
            return damage;

        var resist = 0;
        var weak = 0;
        foreach (var entry in comp.Entries)
        {
            if (!CFDamageTypes.Same(_prototypes, entry.DamageType, damageType))
                continue;

            var amount = entry.Amount;
            if (entry.HalfLevel)
                amount = Math.Max(amount, Math.Max(1, (CompOrNull<CFCombatantComponent>(target)?.Level ?? 1) / 2));

            if (entry.Weakness)
                weak = Math.Max(weak, amount);
            else
                resist = Math.Max(resist, amount);
        }

        return Math.Max(0, damage + weak - resist);
    }

    private void ExpireResistances(TimeSpan now)
    {
        var query = EntityQueryEnumerator<CFResistancesComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            comp.Entries.RemoveAll(e => e.Expires != TimeSpan.Zero && e.Expires <= now);
        }
    }

    #endregion

    #region Persistent damage

    /// <summary>
    /// Persistent damage of a type the creature already has is not added twice. The larger one stays.
    /// </summary>
    public void AddPersistent(EntityUid uid, ProtoId<CFDamageTypePrototype> type, int dice, int die, int flat, EntityUid? source)
    {
        if (dice <= 0 && flat <= 0)
            return;

        var comp = EnsureComp<CFPersistentDamageComponent>(uid);
        if (comp.Entries.Count == 0)
            comp.NextTick = _timing.CurTime + Round;

        var average = dice * (die + 1) / 2f + flat;
        foreach (var entry in comp.Entries)
        {
            if (entry.DamageType != type)
                continue;

            if (average > entry.Dice * (entry.Die + 1) / 2f + entry.Flat)
            {
                entry.Dice = dice;
                entry.Die = die;
                entry.Flat = flat;
                entry.Source = source;
            }

            return;
        }

        comp.Entries.Add(new CFActivePersistentDamage
        {
            DamageType = type,
            Dice = dice,
            Die = die,
            Flat = flat,
            Source = source,
        });
    }

    private void TickPersistent(TimeSpan now)
    {
        var query = EntityQueryEnumerator<CFPersistentDamageComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextTick > now)
                continue;

            comp.NextTick = now + Round;
            for (var i = comp.Entries.Count - 1; i >= 0; i--)
            {
                var entry = comp.Entries[i];
                var amount = CFEffectSystem.Roll(_random, entry.Dice, entry.Die, entry.Flat, critical: false);
                var source = entry.Source is { } who && Exists(who) ? who : uid;
                _effects.DealDamage(source, uid, amount, entry.DamageType, critical: false);

                if (_random.Next(1, 21) >= PersistentFlatCheckDc)
                    comp.Entries.RemoveAt(i);
            }

            if (comp.Entries.Count == 0)
                RemCompDeferred<CFPersistentDamageComponent>(uid);
        }
    }

    #endregion
}
