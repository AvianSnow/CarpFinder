using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

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

    /// <summary>An enemy within reach critically failed a Strike against this creature.</summary>
    AttackCriticallyMissed,

    /// <summary>An enemy inside this creature's aura is about to damage it.</summary>
    WouldBeDamaged,

    /// <summary>An enemy is about to damage an ally, and both are inside this creature's aura.</summary>
    AllyWouldBeDamaged,

    /// <summary>An ally close by is about to take damage. This creature takes it instead.</summary>
    InterceptsDamage,

    /// <summary>The creature this one has Exploited is about to damage it or an ally close by.</summary>
    ExploitedWouldDamage,

    /// <summary>A Strike has critically hit this creature, and its damage is about to land.</summary>
    CriticallyHit,

    /// <summary>An attack is about to hit an ally within <see cref="CFReaction.RangeFeet"/>.</summary>
    AllyWouldBeHit,

    /// <summary>An enemy this creature can see, within <see cref="CFReaction.RangeFeet"/>, casts a spell.</summary>
    EnemyCastsSpell,

    /// <summary>A skill check this creature rolled has come up short of a better result.</summary>
    SkillCheckWouldFail,

    /// <summary>An ally within <see cref="CFReaction.RangeFeet"/> would drop to 0 Hit Points and would not die outright.</summary>
    AllyWouldDrop,

    /// <summary>This creature is being grabbed. The grab does not take hold.</summary>
    Grabbed,

    /// <summary>This creature is becoming sickened.</summary>
    Sickened,

    /// <summary>This creature is becoming frightened.</summary>
    Frightened,

    /// <summary>An ally within <see cref="CFReaction.RangeFeet"/> has dropped to 0 Hit Points.</summary>
    AllyDropped,

    /// <summary>An enemy's melee attack has missed this creature (Sudden Shift).</summary>
    MeleeMissed,

    /// <summary>A creature within <see cref="CFReaction.RangeFeet"/> has died, or an undead one has been destroyed (Death's Call).</summary>
    CreatureDied,

    /// <summary>
    /// An ally within <see cref="CFReaction.RangeFeet"/> has failed to get the best result on a save, against an effect with
    /// <see cref="CFReaction.Trait"/> when it is set. The ally's result is one degree better (Share Burden).
    /// </summary>
    AllySaveWouldFail,
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

    /// <summary>How long before it can be used again, as "once per 10 minutes". 0 means no wait.</summary>
    [DataField]
    public float CooldownSeconds;

    [DataField]
    public bool RangedOnly;

    /// <summary>Only against a melee attack, such as a creature that touches this one.</summary>
    [DataField]
    public bool MeleeOnly;

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

    /// <summary>
    /// With <see cref="CFReactionTrigger.SaveWouldFail"/>: the save uses the modifier of an ally within this many feet, when
    /// that is better than the creature's own (Ally's Shelter). 0 means off.
    /// </summary>
    [DataField]
    public int AllyModifierFeet;

    /// <summary>
    /// With <see cref="CFReactionTrigger.AttackWouldHit"/>: the attack is rolled against this skill's DC and not against AC
    /// (Hunter's Defense).
    /// </summary>
    [DataField]
    public ProtoId<CFAbilitySkillPrototype>? AcFromSkill;

    /// <summary>Only against an attacker with one of these traits. Empty means any.</summary>
    [DataField]
    public List<ProtoId<CFTraitPrototype>> AttackerTraits = new();

    /// <summary>
    /// With <see cref="CFReactionTrigger.CriticallyHit"/>: also answers a critical hit on an ally this creature can see who
    /// has this trait (Strix Vengeance).
    /// </summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? AllyTrait;

    /// <summary>With <see cref="CFReactionTrigger.Sickened"/>, how much comes off the sickened value.</summary>
    [DataField]
    public int SickenedLess;

    /// <summary>With <see cref="CFReactionTrigger.Frightened"/>, how much comes off the frightened value.</summary>
    [DataField]
    public int FrightenedLess;

    /// <summary>Only under a morph or polymorph effect: a battle form, or an animal Rage (Creature Comforts).</summary>
    [DataField]
    public bool WhileMorphed;

    /// <summary>
    /// The creature rushes toward whoever set the reaction off, up to its Speed, and that move sets off no reactions: an enemy
    /// that left its reach (No Escape, Reactive Pursuit) or an ally that dropped (Rapid Response).
    /// </summary>
    [DataField]
    public bool Follows;

    /// <summary>Only while raging.</summary>
    [DataField]
    public bool WhileRaging;

    /// <summary>The creature ends up prone after using it, as Hit the Dirt! does.</summary>
    [DataField]
    public bool LeavesProne;

    /// <summary>The creature is off-guard for 6 seconds after using it.</summary>
    [DataField]
    public bool LeavesOffGuard;

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

    /// <summary>With <see cref="CFReactionTrigger.SkillCheckWouldFail"/>, only these skills. Empty means any.</summary>
    [DataField]
    public List<string> Skills = new();

    /// <summary>A <see cref="CFReactionTrigger.SaveWouldFail"/> reaction that answers failed skill checks too, as Halfling Luck does.</summary>
    [DataField]
    public bool AndSkillChecks;

    /// <summary>Only for a skill the creature is trained in.</summary>
    [DataField]
    public bool NeedsTrained;

    /// <summary>A failed skill check's result becomes this plus the creature's proficiency bonus instead. 0 means off.</summary>
    [DataField]
    public int ResultOfProficiency;

    /// <summary>A critical failure becomes a plain failure.</summary>
    [DataField]
    public bool CriticalFailureIsFailure;

    /// <summary>Only against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>Only against the creature this one has Exploited.</summary>
    [DataField]
    public bool ExploitOnly;

    /// <summary>Only while holding this kind of implement.</summary>
    [DataField]
    public CFImplementKind? Implement;

    /// <summary>Only against the creature this one is hunting.</summary>
    [DataField]
    public bool PreyOnly;

    /// <summary>A free action with a trigger. It does not use the creature's reaction.</summary>
    [DataField]
    public bool Free;

    /// <summary>Makes a melee Strike against the creature that set it off, if it is in reach.</summary>
    [DataField]
    public bool Strikes;

    /// <summary>With a damage trigger, this much comes off the damage.</summary>
    [DataField]
    public int ResistFlat;

    /// <summary>The creature's level also comes off the damage.</summary>
    [DataField]
    public bool ResistLevel;

    /// <summary>Half the creature's level also comes off the damage.</summary>
    [DataField]
    public bool ResistHalfLevel;

    /// <summary>
    /// With <see cref="CFReactionTrigger.EnemyCastsSpell"/>: spends a prepared charge of the same spell to counteract it,
    /// at that charge's rank (Counterspell).
    /// </summary>
    [DataField]
    public bool CounterspellPrepared;

    /// <summary>
    /// With <see cref="CFReactionTrigger.EnemyCastsSpell"/>: spends a spell slot to counteract the spell, the one the spell
    /// would have cost or the next one left above it (Counter Thought). <see cref="Trait"/> limits which spells.
    /// </summary>
    [DataField]
    public bool CounterspellSlot;

    /// <summary>This attribute's modifier also comes off the damage, when it is positive.</summary>
    [DataField]
    public CFAbility? ResistAbility;

    /// <summary>The die of extra damage this creature takes. One die, and one more at levels 5, 9 and 13.</summary>
    [DataField]
    public int TakesExtraDie;

    /// <summary>For 6 seconds, this creature's Strikes against the enemy deal this much more.</summary>
    [DataField]
    public int MarkDamage;

    /// <summary>For 6 seconds, this creature's Strikes against the enemy deal one die of this size more.</summary>
    [DataField]
    public int MarkDamageDie;

    /// <summary>The ally rolls to Escape from a grab, for free.</summary>
    [DataField]
    public bool FreesAlly;

    /// <summary>With <see cref="CFReactionTrigger.InterceptsDamage"/>, the damage types it covers. Empty means every type.</summary>
    [DataField]
    public List<string> DamageTypes = new();

    /// <summary>With <see cref="CFReactionTrigger.InterceptsDamage"/>, how close the ally has to be.</summary>
    [DataField]
    public int RangeFeet = 10;

    /// <summary>With <see cref="RangeFeet"/>, how close the ally has to be when the damage comes from this creature's taunted enemy.</summary>
    [DataField]
    public int TauntedRangeFeet = 15;

    /// <summary>Runs on the enemy that set this off.</summary>
    [DataField]
    public List<CFEffect> OnEnemy = new();

    /// <summary>Runs on this creature when the reaction fires (Sudden Shift's Step and concealment).</summary>
    [DataField]
    public List<CFEffect> OnSelf = new();

    /// <summary>Focus Points the reaction costs: a focus spell cast as a reaction.</summary>
    [DataField]
    public int FocusPoints;

    /// <summary>
    /// With <see cref="CFReactionTrigger.InterceptsDamage"/>: only this much damage per spell rank is taken in the ally's
    /// place, and the ally takes the rest (Protector's Sacrifice). 0 takes all of it.
    /// </summary>
    [DataField]
    public int RedirectPerRank;

    /// <summary>
    /// With <see cref="CFReactionTrigger.CreatureDied"/>: temporary Hit Points for a minute equal to the dead creature's level
    /// plus this creature's spellcasting modifier, doubled for an undead one (Death's Call).
    /// </summary>
    [DataField]
    public bool TempHpFromDeath;
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

    /// <summary>When <see cref="CFReaction.CooldownSeconds"/> lets it be used again.</summary>
    [DataField]
    public TimeSpan ReadyAt;

    /// <summary>Where it came from, such as <c>feat:CFNimbleDodge</c>. Its place in <see cref="CFReactionOrderComponent"/>.</summary>
    [DataField]
    public string Key = "";
}

/// <summary>
/// The order a creature's reactions get their chance in, first to last. For each trigger, the first one that would change
/// the outcome is the one that fires. A new reaction goes above every lower-level one. The player can reorder them on the
/// character sheet. It holds the built-in reactions as well (<see cref="CFReactionSystem.ReactiveStrikeKey"/> and so on).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFReactionOrderComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<CFReactionEntry> Entries = new();
}

[DataDefinition, Serializable, NetSerializable]
public partial struct CFReactionEntry
{
    [DataField]
    public string Key;

    [DataField]
    public LocId Name;

    /// <summary>The level of the feat or feature that gave it.</summary>
    [DataField]
    public int Level;

    public CFReactionEntry(string key, LocId name, int level)
    {
        Key = key;
        Name = name;
        Level = level;
    }
}

/// <summary>Sent by the character sheet: every reaction key, in the new order. The server checks it.</summary>
[Serializable, NetSerializable]
public sealed class CFReorderReactionsEvent : EntityEventArgs
{
    public List<string> Keys = new();
}
