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
            return SpareLeft(book, rank) > 0;

        return !book.Repertoire.Contains(spell);
    }

    /// <summary>
    /// Pays for one cast, from the highest ready charge when the spell is prepared at several ranks. Call
    /// <see cref="CanCast"/> first. A spell granted some other way costs nothing.
    /// </summary>
    /// <returns>The rank of the slot it used, which is the rank the spell resolves at.</returns>
    public static int Spend(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, int rank)
    {
        var best = -1;
        for (var i = 0; i < book.PreparedSlots.Count; i++)
        {
            var slot = book.PreparedSlots[i];
            if (slot.Spell == spell && !slot.Spent && (best < 0 || slot.Rank > book.PreparedSlots[best].Rank))
                best = i;
        }

        if (best >= 0)
        {
            var slot = book.PreparedSlots[best];
            slot.Spent = true;
            book.PreparedSlots[best] = slot;
            AddExpended(book, slot.Rank);
            return slot.Rank;
        }

        if (book.SpareSlotSpells.Contains(spell) && SpareLeft(book, rank) > 0)
            AddExpended(book, rank);

        return rank;
    }

    /// <summary>
    /// The rank a spontaneous caster casts this spell at: the highest one it knows the spell at with a slot left. Zero if
    /// there is none. A spell it did not learn into a slot goes by its own rank.
    /// </summary>
    public static int SpontaneousRank(CFSpellbookComponent book, ProtoId<CFSpellPrototype> spell, int baseRank)
    {
        var known = false;
        var best = 0;
        foreach (var entry in book.KnownAtRank)
        {
            if (entry.Spell != spell)
                continue;

            known = true;
            if (entry.Rank > best && SlotLeft(book, entry.Rank))
                best = entry.Rank;
        }

        if (known)
            return best;

        return SlotLeft(book, baseRank) ? baseRank : 0;
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
