using System.Linq;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Character;

[Serializable, NetSerializable]
public enum CFSpellTradition
{
    Arcane,
    Divine,
    Occult,
    Primal,
}

[Serializable, NetSerializable]
public enum CFSpellActionCost
{
    One,
    Two,
    Three,
    Reaction,
    Free,
    /// <summary>The spell can be cast with one, two, or three actions.</summary>
    Variable,
}

/// <summary>
/// One Carpfinder spell. Text fields are short mechanical notes, not source-book prose.
/// </summary>
[Prototype("cfSpell")]
public sealed partial class CFSpellPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name { get; private set; } = "";

    /// <summary>0 is a cantrip.</summary>
    [DataField]
    public int Rank;

    [DataField]
    public List<CFSpellTradition> Traditions = new();

    [DataField]
    public CFRarity Rarity = CFRarity.Common;

    [DataField]
    public List<ProtoId<CFTraitPrototype>> Traits = new();

    [DataField]
    public CFSpellActionCost ActionCost = CFSpellActionCost.Two;

    [DataField]
    public string Components = "";

    [DataField]
    public string Range = "";

    [DataField]
    public string Area = "";

    [DataField]
    public string Targets = "";

    [DataField]
    public string Duration = "";

    /// <summary>Save or attack, such as "Reflex" or "Spell attack".</summary>
    [DataField]
    public string Defense = "";

    [DataField]
    public string Summary = "";

    [DataField]
    public string CriticalSuccess = "";

    [DataField]
    public string Success = "";

    [DataField]
    public string Failure = "";

    [DataField]
    public string CriticalFailure = "";

    [DataField]
    public string Heightened = "";

    public bool HasTradition(CFSpellTradition tradition)
    {
        return Traditions.Contains(tradition);
    }
}

public readonly record struct CFSpellRankSlots(int Rank, int Count);

public static class CFSpellSlots
{
    public const int BonusRank = -1;

    /// <summary>The slots a class has at <paramref name="level"/>: none for a class that does not cast. The lobby builds level 1.</summary>
    public static IReadOnlyList<CFSpellRankSlots> ForClass(CFClassPrototype? cls, int level = 1)
    {
        var ranks = new List<CFSpellRankSlots>();
        var casting = cls?.Spellcasting;
        if (casting == null)
            return ranks;

        if (casting.Cantrips > 0)
            ranks.Add(new CFSpellRankSlots(0, casting.Cantrips));

        var slots = casting.SlotsAt(level);
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i] > 0)
                ranks.Add(new CFSpellRankSlots(i + 1, slots[i]));
        }

        return ranks;
    }

    /// <summary>The tradition a build's class casts from, or null when its class does not cast.</summary>
    public static CFSpellTradition? TraditionOf(IPrototypeManager prototypes, CFCharacterBuildData build)
    {
        if (build.Class is not { } classId
            || !prototypes.TryIndex(classId, out CFClassPrototype? cls)
            || cls.Spellcasting is not { } casting)
        {
            return null;
        }

        return CFClassChoices.Tradition(prototypes, build, casting.Tradition);
    }

    /// <summary>
    /// The spell picks a build may have at all: none for a class that does not cast, and otherwise only spells of the
    /// tradition it casts from. A build comes from a client, so the server reads picks through this.
    /// </summary>
    public static List<CFSpellPick> Allowed(IPrototypeManager prototypes, CFCharacterBuildData build)
    {
        var result = new List<CFSpellPick>();
        if (TraditionOf(prototypes, build) is not { } tradition)
            return result;

        foreach (var pick in build.Spells)
        {
            if (pick.HasSpell && prototypes.TryIndex(pick.Spell, out var spell) && spell.HasTradition(tradition))
                result.Add(pick);
        }

        return result;
    }

    /// <summary>
    /// The builder picks a character has by the time it has these slots: the first of each rank, up to that rank's slots.
    /// A build comes from a client, so a spell above its slot's rank is left out.
    /// </summary>
    public static List<CFSpellPick> Within(IEnumerable<CFSpellPick> picks, IReadOnlyList<int> slots, IPrototypeManager prototypes)
    {
        var result = new List<CFSpellPick>();
        var taken = new int[slots.Count];
        foreach (var pick in picks.OrderBy(spell => spell.Rank).ThenBy(spell => spell.Index))
        {
            if (!pick.HasSpell || pick.Rank < 0 || pick.Rank >= slots.Count)
                continue;

            if (!prototypes.TryIndex(pick.Spell, out var spell) || spell.Rank > pick.Rank || taken[pick.Rank] >= slots[pick.Rank])
                continue;

            taken[pick.Rank]++;
            result.Add(pick);
        }

        return result;
    }

    public static string ActionSymbols(CFSpellActionCost cost)
    {
        return cost switch
        {
            CFSpellActionCost.One => "◆",
            CFSpellActionCost.Two => "◆◆",
            CFSpellActionCost.Three => "◆◆◆",
            CFSpellActionCost.Reaction => "↺",
            CFSpellActionCost.Free => "◇",
            CFSpellActionCost.Variable => "◆–◆◆◆",
            _ => "",
        };
    }
}
