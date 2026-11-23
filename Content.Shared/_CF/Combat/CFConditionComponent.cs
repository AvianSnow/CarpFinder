using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Short combat conditions Strikes and movement read.
/// Timed fields are server-only. The flags replicate so movement and examine stay honest.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFConditionComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Frightened;

    /// <summary>Until this time, frightened does not ease by itself below 1 (Aura of Despair).</summary>
    [DataField]
    public TimeSpan FrightenedFloorUntil;

    [DataField, AutoNetworkedField]
    public bool Grabbed;

    [DataField, AutoNetworkedField]
    public bool Immobilized;

    [DataField, AutoNetworkedField]
    public bool Concealed;

    /// <summary>Waiting for this creature's next check. Negative when the help went wrong.</summary>
    public int AidBonus;

    public TimeSpan AidUntil;

    /// <summary>Creatures this one has tried to Recall Knowledge on.</summary>
    public HashSet<EntityUid> Recalled = new();

    /// <summary>Flat check a creature must pass to affect this one. 5 is concealed. 11 is hidden.</summary>
    [DataField, AutoNetworkedField]
    public int FlatCheckDc;

    /// <summary>Casting or Striking ends this concealment. Invisibility does. Blur does not.</summary>
    [DataField, AutoNetworkedField]
    public bool ConcealBreaks;

    [DataField, AutoNetworkedField]
    public bool Sanctuary;

    [DataField, AutoNetworkedField]
    public int SanctuaryDc;

    [DataField, AutoNetworkedField]
    public bool Charmed;

    [DataField, AutoNetworkedField]
    public bool Calmed;

    /// <summary>Darkvision from an elixir or spell, which ends. A creature born with it never has this set.</summary>
    [DataField, AutoNetworkedField]
    public bool TimedDarkvision;

    [DataField, AutoNetworkedField]
    public bool Blinded;

    [DataField, AutoNetworkedField]
    public int Shoves;

    [DataField]
    public TimeSpan LastShove;

    /// <summary>When the next Escape attempt can happen.</summary>
    [DataField]
    public TimeSpan NextEscape;

    /// <summary>Hardness of a shield spell that has not blocked yet. 0 means none.</summary>
    [DataField, AutoNetworkedField]
    public int ShieldHardness;

    [DataField, AutoNetworkedField]
    public bool OffGuardAll;

    public EntityUid? Grabber;

    /// <summary>When a grab that only lasts a while lets go, as Combat Grab's does. Zero means it holds until escaped.</summary>
    public TimeSpan GrabbedUntil;

    public EntityUid? Charmer;

    public HashSet<EntityUid> OffGuardTo = new();

    /// <summary>Creatures that Feinted this one: their melee attacks find it off-guard until the time.</summary>
    public Dictionary<EntityUid, TimeSpan> FeintedUntil = new();

    /// <summary>When this creature last cast a spell. A Superstition Barbarian hits harder for an hour after. Server-only.</summary>
    [DataField]
    public TimeSpan? LastCastAt;

    /// <summary>Nudge Fate: until this time, the next save or skill check a +1 would improve gets it. Server-only.</summary>
    [DataField]
    public TimeSpan NudgedUntil;

    /// <summary>Of <see cref="FeintedUntil"/>, the Feints that end with that creature's next melee attack.</summary>
    public HashSet<EntityUid> FeintedOnce = new();

    /// <summary>Off-guard to every melee attack until this time (a Scoundrel's critical Feint).</summary>
    public TimeSpan FeintedAllUntil;

    /// <summary>This creature takes -2 circumstance to attack rolls against these creatures until the time (Overextending Feint).</summary>
    public Dictionary<EntityUid, TimeSpan> OverextendedUntil = new();

    /// <summary>Of <see cref="OverextendedUntil"/>, the penalties that end with the next attack roll.</summary>
    public HashSet<EntityUid> OverextendedOnce = new();

    /// <summary>Of <see cref="OverextendedUntil"/>, a penalty other than 2 (Charming Push's 1).</summary>
    public Dictionary<EntityUid, int> OverextendedPenalty = new();

    public Dictionary<EntityUid, TimeSpan> DemoralizedUntil = new();

    public TimeSpan ImmobilizedUntil;

    /// <summary>Timed clumsy value, on top of encumbrance. The higher of the two counts.</summary>
    [DataField, AutoNetworkedField]
    public int ClumsyValue;

    public TimeSpan ClumsyUntil;

    /// <summary>
    /// Enfeebled, drained, doomed, dazzled, fatigued, deafened and fascinated, by kind. The value is the condition's value,
    /// or 1 for one that has none. Read through <c>CFConditionSystem.Value</c>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<CFConditionKind, int> Values = new();

    /// <summary>When each entry of <see cref="Values"/> ends.</summary>
    public Dictionary<CFConditionKind, TimeSpan> ValueUntil = new();

    /// <summary>Stupefied value: a penalty to Will saves and Perception.</summary>
    [DataField, AutoNetworkedField]
    public int StupefiedValue;

    public TimeSpan StupefiedUntil;

    /// <summary>Sickened value. The penalties themselves are timed bonuses. This is what the HUD names.</summary>
    [DataField, AutoNetworkedField]
    public int SickenedValue;

    public TimeSpan SickenedUntil;

    public TimeSpan ConcealedUntil;

    public TimeSpan SanctuaryUntil;

    public TimeSpan CharmedUntil;

    public TimeSpan CalmedUntil;

    public TimeSpan DarkvisionUntil;

    public TimeSpan BlindUntil;

    public TimeSpan OffGuardAllUntil;

    public Dictionary<EntityUid, TimeSpan> OffGuardUntil = new();
}

/// <summary>Raised on a creature the asleep condition has just knocked out. Stock sleep has its own event.</summary>
[ByRefEvent]
public readonly record struct CFPutToSleepEvent;

/// <summary>Conditions an effect can apply through <see cref="CFConditionSystem.ApplyCondition"/>.</summary>
public enum CFConditionKind : byte
{
    /// <summary>Value is the frightened value.</summary>
    Frightened,
    Prone,

    /// <summary>Knocked down and unconscious for the duration (sleep).</summary>
    Asleep,
    Paralyzed,
    Immobilized,

    /// <summary>DC 5 flat check to target (blur).</summary>
    Concealed,

    /// <summary>DC 11 flat check to target. Ends when the creature attacks or casts (invisibility).</summary>
    Invisible,

    /// <summary>Attackers must pass a Will save against the effect's DC.</summary>
    Sanctuary,
    Charmed,
    Calmed,
    Blinded,
    Darkvision,

    /// <summary>Value is the clumsy value.</summary>
    Clumsy,

    /// <summary>Value is the stupefied value.</summary>
    Stupefied,

    /// <summary>Value is the sickened value: a status penalty to every check and DC.</summary>
    Sickened,

    /// <summary>Blocks damage once. Value is the hardness (shield cantrip).</summary>
    SpellShield,
    OffGuard,

    /// <summary>Value is the enfeebled value: a penalty to melee attack and damage rolls and Strength-based checks.</summary>
    Enfeebled,

    /// <summary>Value is the drained value: a penalty to Fortitude saves. The lost Hit Points are not taken.</summary>
    Drained,

    /// <summary>Value is the doomed value: the creature dies at a dying value that much lower.</summary>
    Doomed,

    /// <summary>Everything is concealed to the creature: a DC 5 flat check to target anything.</summary>
    Dazzled,

    /// <summary>Vitality heals the creature only half as much (Touch of Undeath).</summary>
    HealingHalved,

    /// <summary>-1 to AC and saves.</summary>
    Fatigued,

    /// <summary>-2 to Perception.</summary>
    Deafened,

    /// <summary>-2 to Perception and skill checks.</summary>
    Fascinated,

    /// <summary>Not kept: the creature loses that many actions at once.</summary>
    Stunned,
}
