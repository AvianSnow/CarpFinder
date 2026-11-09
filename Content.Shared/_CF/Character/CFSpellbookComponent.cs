using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Character;

/// <summary>
/// Spell slots and focus points copied from the class at spawn.
/// Index 0 of <see cref="Slots"/> is cantrips. Index 1 is rank 1.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFSpellbookComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<int> Slots = new();

    /// <summary>
    /// Expended slots per rank, same indexing as <see cref="Slots"/>.
    /// A prepared caster's count here matches its spent <see cref="PreparedSlots"/>, plus any spare slots used.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<int> Expended = new();

    [DataField, AutoNetworkedField]
    public int FocusPoints;

    [DataField, AutoNetworkedField]
    public int FocusSpent;

    /// <summary>A prepared caster spends a charge of the exact spell. Anyone else spends a slot of the rank.</summary>
    [DataField, AutoNetworkedField]
    public bool Prepared;

    /// <summary>What is prepared right now, one entry per charge. Preparing a spell twice is two entries.</summary>
    [DataField, AutoNetworkedField]
    public List<CFPreparedSlot> PreparedSlots = new();

    /// <summary>What the next long rest will prepare. The character sheet's Preparation window edits this.</summary>
    [DataField, AutoNetworkedField]
    public List<CFPreparedSlot> PlannedSlots = new();

    /// <summary>The spells this caster can prepare. A spell not on this list was granted some other way, and is at will.</summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<CFSpellPrototype>> Repertoire = new();

    /// <summary>The ranks a spontaneous caster learned each spell at, one entry per slot it picked the spell for.</summary>
    [DataField, AutoNetworkedField]
    public List<CFPreparedSlot> KnownAtRank = new();

    /// <summary>How many spells each rank can have prepared, same indexing as <see cref="Slots"/>. Excludes spare slots.</summary>
    [DataField, AutoNetworkedField]
    public List<int> PreparableSlots = new();

    /// <summary>Spells that may use a spare slot, one <see cref="Slots"/> has and nothing is prepared in. Divine Font's Heal and Harm.</summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<CFSpellPrototype>> SpareSlotSpells = new();

    /// <summary>An Animist's apparition slots per rank, same indexing as <see cref="Slots"/>. They are spent like a spontaneous caster's.</summary>
    [DataField, AutoNetworkedField]
    public List<int> ApparitionSlots = new();

    /// <summary>Apparition slots used per rank.</summary>
    [DataField, AutoNetworkedField]
    public List<int> ApparitionExpended = new();

    /// <summary>The spells apparition slots can cast.</summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<CFSpellPrototype>> ApparitionSpells = new();

    /// <summary>Spells with a duration the caster keeps going. See <c>CFSustain</c>.</summary>
    [DataField, AutoNetworkedField]
    public List<CFSustainedSpell> Sustaining = new();
}

/// <summary>A spell being sustained.</summary>
[DataDefinition, Serializable, NetSerializable]
public partial struct CFSustainedSpell
{
    [DataField]
    public ProtoId<CFSpellPrototype> Spell;

    [DataField]
    public TimeSpan Started;

    /// <summary>When it was last cast or sustained.</summary>
    [DataField]
    public TimeSpan Last;

    public CFSustainedSpell(ProtoId<CFSpellPrototype> spell, TimeSpan started, TimeSpan last)
    {
        Spell = spell;
        Started = started;
        Last = last;
    }
}

/// <summary>One prepared charge of a spell.</summary>
[DataDefinition, Serializable, NetSerializable]
public partial struct CFPreparedSlot
{
    [DataField]
    public ProtoId<CFSpellPrototype> Spell;

    /// <summary>The slot's rank, an index of <see cref="CFSpellbookComponent.Slots"/>.</summary>
    [DataField]
    public int Rank;

    [DataField]
    public bool Spent;

    public CFPreparedSlot(ProtoId<CFSpellPrototype> spell, int rank, bool spent = false)
    {
        Spell = spell;
        Rank = rank;
        Spent = spent;
    }
}

/// <summary>Sent by the character sheet. The server checks it, then applies it at the next long rest.</summary>
[Serializable, NetSerializable]
public sealed class CFPlanPreparationEvent : EntityEventArgs
{
    public List<CFPreparedSlot> Slots = new();
}
