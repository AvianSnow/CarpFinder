using Content.Shared.CF.Checks;
using Content.Shared.CF.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>What sets off a reaction.</summary>
public enum CFReactionTrigger : byte
{
    /// <summary>A Strike is about to land on this creature.</summary>
    AttackWouldHit,

    /// <summary>A saving throw has come up short of a better result.</summary>
    SaveWouldFail,

    /// <summary>This creature took damage and is still up.</summary>
    Damaged,

    /// <summary>This creature would drop to 0 Hit Points and would not die outright.</summary>
    WouldDrop,

    /// <summary>An enemy left this creature's reach. Walking out and paying 1 action for a Step avoids it.</summary>
    EnemyLeftReach,
}

/// <summary>
/// One reaction a character knows: a trigger, what has to be true, and what it does. Written in YAML under
/// <c>CFGrantReaction</c>. Reactions are automatic. One only fires when it would change the outcome, so it is not wasted.
/// </summary>
[DataDefinition]
public sealed partial class CFReaction
{
    /// <summary>Shown over the creature when the reaction fires.</summary>
    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public CFReactionTrigger Trigger;

    /// <summary>How many times it can be used between long rests. 0 means no limit.</summary>
    [DataField]
    public int PerDay;

    [DataField]
    public bool RangedOnly;

    [DataField]
    public bool NeedsToSeeAttacker;

    [DataField]
    public bool NotEncumbered;

    [DataField]
    public bool NotOffGuard;

    [DataField]
    public bool NeedsFreeHand;

    /// <summary>A circumstance bonus to AC against the triggering attack.</summary>
    [DataField]
    public int AcBonus;

    /// <summary>A circumstance bonus to the triggering save.</summary>
    [DataField]
    public int SaveBonus;

    /// <summary>Gains panache if the attack missed or the save succeeded.</summary>
    [DataField]
    public bool GainsPanache;

    /// <summary>Enters a rage, for a creature that can.</summary>
    [DataField]
    public bool EntersRage;

    /// <summary>With <see cref="CFReactionTrigger.SaveWouldFail"/>, rolls a failed save again.</summary>
    [DataField]
    public bool Rerolls;

    /// <summary>With <see cref="Rerolls"/>, the better result counts. Otherwise the new roll stands, even if worse.</summary>
    [DataField]
    public bool KeepsBetter;

    /// <summary>Only for this save. Null means any.</summary>
    [DataField]
    public CFDefenseType? Save;

    /// <summary>Only against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>A free action with a trigger. It does not use the creature's reaction.</summary>
    [DataField]
    public bool Free;

    /// <summary>Makes a melee Strike against the creature that set it off.</summary>
    [DataField]
    public bool Strikes;
}

/// <summary>The reactions a creature has been granted, and how many daily uses it has spent.</summary>
[RegisterComponent]
public sealed partial class CFReactionsComponent : Component
{
    [DataField]
    public List<CFKnownReaction> Known = new();
}

/// <summary>
/// Enemies that have just run out of this creature's reach, waiting to be answered. The contact that reports it ends inside
/// the physics step, where a Strike must not deal damage, so <see cref="CFReactionSystem"/> answers on its next update.
/// </summary>
[RegisterComponent]
public sealed partial class CFLeftReachComponent : Component
{
    public List<EntityUid> Left = new();
}

[DataDefinition]
public sealed partial class CFKnownReaction
{
    [DataField(required: true)]
    public CFReaction Reaction = default!;

    [DataField]
    public int UsedToday;
}
