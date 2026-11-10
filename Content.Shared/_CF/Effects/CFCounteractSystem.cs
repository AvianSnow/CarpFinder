using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Spells;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Effects;

/// <summary>The spells still working on a creature, so a counteract has something to aim at.</summary>
[RegisterComponent]
public sealed partial class CFLingeringSpellsComponent : Component
{
    [ViewVariables]
    public List<CFLingeringSpell> Spells = new();
}

/// <summary>One spell cast on a creature: who cast it, how strong it was, and what to undo if it is counteracted.</summary>
public sealed class CFLingeringSpell
{
    /// <summary>What its bonuses are filed under in <see cref="CFBonusSystem"/>.</summary>
    public string Key = "";
    public string Name = "";
    public EntityUid Caster;
    public int Rank;
    public int Dc;
    public TimeSpan Expires;
    public bool Bonuses;
    public HashSet<CFConditionKind> Conditions = new();
}

/// <summary>
/// PF2e counteracting. A counteract check is the caster's spell attack modifier against the effect's DC, and the degree
/// decides how high a rank it can end: four ranks above on a critical success, one above on a success, and only a lower
/// rank on a failure. Only spells cast through a spell action are remembered, and only their bonuses and the conditions
/// <see cref="CFConditionSystem.EndCondition"/> can end.
/// </summary>
public sealed partial class CFCounteractSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>
    /// Remembers that a spell has put something lasting on <paramref name="target"/>. Returns the key its bonuses go under,
    /// or null when the source is not a spell.
    /// </summary>
    public string? Note(CFEffectArgs args, EntityUid target, float seconds, CFConditionKind? condition = null, bool bonus = false)
    {
        if (!TryComp(args.Source, out CFSpellComponent? spell))
            return null;

        var now = _timing.CurTime;
        var comp = EnsureComp<CFLingeringSpellsComponent>(target);
        comp.Spells.RemoveAll(old => old.Expires <= now);

        var key = $"spell:{spell.Spell}:{args.User}";
        var entry = comp.Spells.Find(old => old.Key == key);
        if (entry == null)
        {
            entry = new CFLingeringSpell
            {
                Key = key,
                Name = _prototypes.TryIndex(spell.Spell, out var proto) ? proto.Name : spell.Spell.Id,
                Caster = args.User,
            };
            comp.Spells.Add(entry);
        }

        entry.Rank = Math.Max(1, args.Rank);
        entry.Dc = args.Dc;
        var expires = now + TimeSpan.FromSeconds(Math.Max(1f, seconds));
        if (expires > entry.Expires)
            entry.Expires = expires;

        entry.Bonuses |= bonus;
        if (condition is { } kind)
            entry.Conditions.Add(kind);

        return key;
    }

    /// <summary>Whether a counteract check of this degree ends an effect of <paramref name="targetRank"/>.</summary>
    public static bool Counteracts(CFDegree degree, int counteractRank, int targetRank)
    {
        return degree switch
        {
            CFDegree.CriticalSuccess => targetRank <= counteractRank + 3,
            CFDegree.Success => targetRank <= counteractRank + 1,
            CFDegree.Failure => targetRank < counteractRank,
            _ => false,
        };
    }

    /// <summary>
    /// Tries to counteract one spell on <paramref name="target"/>: one cast by the dispeller's enemies first, then the
    /// highest rank. False when there was nothing to aim at.
    /// </summary>
    public bool TryDispel(EntityUid dispeller, EntityUid target, int counteractRank, int modifier, int? d20 = null)
    {
        if (!TryComp(target, out CFLingeringSpellsComponent? comp))
            return false;

        var now = _timing.CurTime;
        comp.Spells.RemoveAll(old => old.Expires <= now);
        CFLingeringSpell? aim = null;
        foreach (var entry in comp.Spells)
        {
            if (aim == null || Better(dispeller, entry, aim))
                aim = entry;
        }

        if (aim == null)
            return false;

        var roll = d20 ?? _random.Next(1, 21);
        var degree = CFDegrees.DegreeOfSuccess(roll, modifier, aim.Dc);
        if (!Counteracts(degree, counteractRank, aim.Rank))
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-dispel-failed", ("spell", aim.Name)), target);
            return true;
        }

        End(target, comp, aim);
        _popup.PopupEntity(Loc.GetString("cf-spell-dispelled", ("spell", aim.Name)), target);
        return true;
    }

    private bool Better(EntityUid dispeller, CFLingeringSpell candidate, CFLingeringSpell current)
    {
        var candidateHostile = _factions.CanHarm(dispeller, candidate.Caster);
        var currentHostile = _factions.CanHarm(dispeller, current.Caster);
        if (candidateHostile != currentHostile)
            return candidateHostile;

        return candidate.Rank > current.Rank;
    }

    private void End(EntityUid target, CFLingeringSpellsComponent comp, CFLingeringSpell spell)
    {
        if (spell.Bonuses)
            _bonuses.RemoveBonusesFrom(target, spell.Key);

        foreach (var kind in spell.Conditions)
        {
            _conditions.EndCondition(target, kind);
        }

        comp.Spells.Remove(spell);
    }
}
