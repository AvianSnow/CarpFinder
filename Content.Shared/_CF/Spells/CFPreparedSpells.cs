using Content.Shared.CF.Character;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Spells;

/// <summary>
/// The rules for a prepared caster's charges, with no game state of their own, so the HUD and the server read the same answer.
/// </summary>
public static class CFPreparedSpells
{
    /// <summary>How many charges of this spell are prepared, and how many are left.</summary>
    public static (int Ready, int Total) Charges(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell)
    {
        var ready = 0;
        var total = 0;
        foreach (var slot in book.PreparedSlots)
        {
            if (slot.Spell != spell)
                continue;

            total++;
            if (!slot.Spent)
                ready++;
        }

        return (ready, total);
    }

    /// <summary>True when this spell shows charges on the HUD, rather than being at will or paid for by a spare slot.</summary>
    public static bool UsesCharges(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell)
    {
        return book.Prepared && (book.Repertoire.Contains(spell) || Charges(book, spell).Total > 0);
    }

    /// <summary>Spare slots of this rank not yet used. Divine Font's are the only ones so far.</summary>
    public static int SpareLeft(CFSpellbookComponent book, int rank)
    {
        if (rank < 0 || rank >= book.Slots.Count)
            return 0;

        var prepared = 0;
        var preparedSpent = 0;
        foreach (var slot in book.PreparedSlots)
        {
            if (slot.Rank != rank)
                continue;

            prepared++;
            if (slot.Spent)
                preparedSpent++;
        }

        var spent = rank < book.Expended.Count ? book.Expended[rank] : 0;
        var spare = book.Slots[rank] - prepared;
        var spareSpent = spent - preparedSpent;
        return Math.Max(0, spare - spareSpent);
    }

    public static bool CanCast(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, int rank)
    {
        if (Charges(book, spell).Ready > 0)
            return true;

        if (book.SpareSlotSpells.Contains(spell))
            return HighestSpare(book, rank) > 0;

        return !book.Repertoire.Contains(spell);
    }

    /// <summary>The highest rank, from <paramref name="baseRank"/> up, with a spare slot left. Zero if none.</summary>
    private static int HighestSpare(CFSpellbookComponent book, int baseRank)
    {
        for (var rank = book.Slots.Count - 1; rank >= Math.Max(1, baseRank); rank--)
        {
            if (SpareLeft(book, rank) > 0)
                return rank;
        }

        return 0;
    }

    /// <summary>
    /// Pays for one cast: from the charge at <paramref name="wanted"/> if there is one, otherwise the highest ready charge.
    /// Call <see cref="CanCast"/> first. A spell granted some other way costs nothing.
    /// </summary>
    /// <returns>The rank of the slot it used, which is the rank the spell resolves at.</returns>
    public static int Spend(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, int rank, int wanted = 0)
    {
        var best = -1;
        var exact = -1;
        for (var i = 0; i < book.PreparedSlots.Count; i++)
        {
            var slot = book.PreparedSlots[i];
            if (slot.Spell != spell || slot.Spent)
                continue;

            if (best < 0 || slot.Rank > book.PreparedSlots[best].Rank)
                best = i;

            if (slot.Rank == wanted)
                exact = i;
        }

        // The menu offers spare slots alongside the charges.
        var spareAsked = exact < 0
                         && wanted >= rank
                         && book.SpareSlotSpells.Contains(spell)
                         && SpareLeft(book, wanted) > 0;
        if (exact >= 0)
            best = exact;

        if (best >= 0 && !spareAsked)
        {
            var slot = book.PreparedSlots[best];
            slot.Spent = true;
            book.PreparedSlots[best] = slot;
            AddExpended(book, slot.Rank);
            return slot.Rank;
        }

        if (!book.SpareSlotSpells.Contains(spell))
            return rank;

        var spare = spareAsked ? wanted : HighestSpare(book, rank);
        if (spare <= 0)
            return rank;

        AddExpended(book, spare);
        return spare;
    }

    /// <summary>
    /// The rank a spontaneous caster casts this spell at: <paramref name="wanted"/> if that is possible, otherwise the highest
    /// that is. Zero if none is.
    /// </summary>
    public static int SpontaneousRank(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, int baseRank, int wanted = 0)
    {
        var ranks = CastableRanks(book, spell, baseRank);
        if (ranks.Count == 0)
            return 0;

        return ranks.Contains(wanted) ? wanted : ranks[^1];
    }

    /// <summary>
    /// Every rank this spell can be cast at right now, lowest first. Prepared: its ready charges, and a spare slot. Spontaneous:
    /// the ranks it was learned at, or every rank from its own for a signature spell. A spell it did not learn into a slot
    /// goes by its own rank.
    /// </summary>
    public static List<int> CastableRanks(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, int baseRank)
    {
        var ranks = new List<int>();
        if (book.Prepared)
        {
            foreach (var slot in book.PreparedSlots)
            {
                if (slot.Spell == spell && !slot.Spent && !ranks.Contains(slot.Rank))
                    ranks.Add(slot.Rank);
            }

            if (book.SpareSlotSpells.Contains(spell))
            {
                for (var rank = Math.Max(1, baseRank); rank < book.Slots.Count; rank++)
                {
                    if (SpareLeft(book, rank) > 0 && !ranks.Contains(rank))
                        ranks.Add(rank);
                }
            }

            ranks.Sort();
            return ranks;
        }

        var learned = false;
        foreach (var entry in book.KnownAtRank)
        {
            if (entry.Spell != spell)
                continue;

            learned = true;
            if (SlotLeft(book, entry.Rank) && !ranks.Contains(entry.Rank))
                ranks.Add(entry.Rank);
        }

        if (learned && book.SignatureSpells.Contains(spell))
        {
            for (var rank = baseRank; rank < book.Slots.Count; rank++)
            {
                if (SlotLeft(book, rank) && !ranks.Contains(rank))
                    ranks.Add(rank);
            }
        }

        if (!learned && SlotLeft(book, baseRank))
            ranks.Add(baseRank);

        ranks.Sort();
        return ranks;
    }

    private static bool SlotLeft(CFSpellbookComponent book, int rank)
    {
        if (rank <= 0 || rank >= book.Slots.Count)
            return false;

        var spent = rank < book.Expended.Count ? book.Expended[rank] : 0;
        return spent < book.Slots[rank];
    }

    /// <summary>Spends any one charge of this rank, for abilities that burn a slot without casting. False if none is left.</summary>
    public static bool TrySpendAny(CFSpellbookComponent book, int rank)
    {
        for (var i = 0; i < book.PreparedSlots.Count; i++)
        {
            var slot = book.PreparedSlots[i];
            if (slot.Rank != rank || slot.Spent)
                continue;

            slot.Spent = true;
            book.PreparedSlots[i] = slot;
            AddExpended(book, rank);
            return true;
        }

        return false;
    }

    /// <summary>A long rest: what was planned becomes what is prepared, every charge ready.</summary>
    public static void Refresh(CFSpellbookComponent book)
    {
        book.PreparedSlots.Clear();
        foreach (var planned in book.PlannedSlots)
            book.PreparedSlots.Add(new CFPreparedSlot(planned.Spell, planned.Rank));
    }

    /// <summary>True when a plan fits the slots and only names spells this caster can prepare.</summary>
    public static bool IsValidPlan(CFSpellbookComponent book, List<CFPreparedSlot> plan)
    {
        var perRank = new int[book.PreparableSlots.Count];
        foreach (var slot in plan)
        {
            if (slot.Spent || slot.Rank <= 0 || slot.Rank >= perRank.Length || !book.Repertoire.Contains(slot.Spell))
                return false;

            perRank[slot.Rank]++;
            if (perRank[slot.Rank] > book.PreparableSlots[slot.Rank])
                return false;
        }

        return true;
    }

    private static void AddExpended(CFSpellbookComponent book, int rank)
    {
        while (book.Expended.Count <= rank)
            book.Expended.Add(0);

        book.Expended[rank]++;
    }
}
