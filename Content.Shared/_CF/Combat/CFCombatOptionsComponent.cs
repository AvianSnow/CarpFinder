using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Class features and chosen feats that change a Strike. Granted at spawn.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFCombatOptionsComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool SneakAttack;

    /// <summary>Finesse melee Strikes may add Dexterity to damage instead of Strength (the Thief racket).</summary>
    [DataField, AutoNetworkedField]
    public bool FinesseDexterity;

    /// <summary>Sneak Attack works with any weapon within the die limits (Ruffian).</summary>
    [DataField, AutoNetworkedField]
    public bool AnyWeaponSneak;

    /// <summary>The first hit on your prey each turn deals 1d8 precision (Precision edge).</summary>
    [DataField, AutoNetworkedField]
    public bool PreyPrecision;

    /// <summary>Multiple attack penalty is smaller against your prey (Flurry edge).</summary>
    [DataField, AutoNetworkedField]
    public bool PreyFlurry;

    /// <summary>+1 circumstance AC against your prey (Outwit edge).</summary>
    [DataField, AutoNetworkedField]
    public bool PreyOutwit;

    /// <summary>Frightened is 1 lower whenever you gain it.</summary>
    [DataField, AutoNetworkedField]
    public bool Bravery;

    /// <summary>Not off-guard to flanking creatures of your level or lower.</summary>
    [DataField, AutoNetworkedField]
    public bool DenyAdvantage;

    /// <summary>Frightened creatures are off-guard to you.</summary>
    [DataField, AutoNetworkedField]
    public bool DreadStriker;

    /// <summary>A critical hit that deals damage frightens the target.</summary>
    [DataField, AutoNetworkedField]
    public bool BrutalBeating;

    /// <summary>A critical hit that deals damage leaves the target off-guard to you.</summary>
    [DataField, AutoNetworkedField]
    public bool UnbalancingBlow;

    /// <summary>Range increments are doubled.</summary>
    [DataField, AutoNetworkedField]
    public bool FarShot;

    /// <summary>Thrown range increments are 10 feet longer.</summary>
    [DataField, AutoNetworkedField]
    public bool StrongArm;

    /// <summary>Walking out of reach needs no action (Mobility).</summary>
    [DataField, AutoNetworkedField]
    public bool FreeStep;

    /// <summary>A successful Grapple also deals the grappler's Strength modifier in bludgeoning damage.</summary>
    [DataField, AutoNetworkedField]
    public bool CrushingGrab;

    /// <summary>Crossbow Strikes deal 2 extra precision damage.</summary>
    [DataField, AutoNetworkedField]
    public bool SlingerPrecision;

    /// <summary>A successful Shove also deals the shover's Strength modifier in bludgeoning damage.</summary>
    [DataField, AutoNetworkedField]
    public bool PunishingShove;

    /// <summary>A simple weapon's damage die is one step larger.</summary>
    [DataField, AutoNetworkedField]
    public bool HumbleStrikes;

    /// <summary>
    /// Bespell Strikes and the like: after casting a spell, the next Strike that hits deals one die of this size more.
    /// 0 means the character has no such feat.
    /// </summary>
    [DataField]
    public int BespellDie;

    /// <summary>The damage type of <see cref="BespellDie"/>.</summary>
    [DataField]
    public string BespellType = "Force";

    /// <summary>A cantrip sets it off too, as Psi Strikes allows.</summary>
    [DataField]
    public bool BespellCantrips;

    /// <summary>A spell has been cast, and the next Strike before this time gets <see cref="BespellDie"/>.</summary>
    [DataField]
    public TimeSpan BespellUntil;

    /// <summary>A creature identified with Recall Knowledge is off-guard to this one: 6 seconds, or a minute on a critical success (Mastermind).</summary>
    [DataField]
    public bool MastermindRecall;

    /// <summary>Hunt Prey comes with a free Recall Knowledge about the prey (Monster Hunter).</summary>
    [DataField]
    public bool MonsterHunter;

    /// <summary>Devise a Stratagem comes with a free Recall Knowledge about the target (Known Weaknesses).</summary>
    [DataField]
    public bool KnownWeaknesses;

    /// <summary>What this creature summons is a thing of thought: it resists physical damage and is weak to force and mental (Thoughtform Summoning).</summary>
    [DataField]
    public bool ThoughtformSummoning;

    /// <summary>An enemy that fails its save against this creature's emotion spell is off-guard to it for 6 seconds (Emotional Push).</summary>
    [DataField]
    public bool EmotionalPush;

    /// <summary>Rages for free as a fight begins (Quick-Tempered). See <see cref="CFFightSystem"/>.</summary>
    [DataField]
    public bool QuickTempered;

    /// <summary>A creature that has not acted yet is off-guard to this one when it starts the fight (Surprise Attack).</summary>
    [DataField]
    public bool SurpriseAttack;

    /// <summary>A raised shield that gives +1 to AC, which is a buckler, gives +2 (Elegant Buckler).</summary>
    [DataField]
    public bool ElegantBuckler;

    /// <summary>A shield bash rolls a damage die one step larger (Shield Warfare).</summary>
    [DataField]
    public bool ShieldWarfare;

    /// <summary>Flurry of Blows that hurts its target may stun it (Stunning Blows).</summary>
    [DataField]
    public bool StunningBlows;

    /// <summary>A critical hit with a stored Devise a Stratagem roll that deals piercing or slashing damage also bleeds (Scalpel's Point).</summary>
    [DataField]
    public bool ScalpelsPoint;

    /// <summary>+4 circumstance to AC against reactions set off by this creature's movement (Guarded Movement).</summary>
    [DataField]
    public bool GuardedMovement;

    /// <summary>A Leap goes 5 feet further (Dancing Leaf).</summary>
    [DataField]
    public bool DancingLeaf;

    /// <summary>Shield Block against an enemy in reach also shoves it 5 feet away (Aggressive Block).</summary>
    [DataField, AutoNetworkedField]
    public bool AggressiveBlock;

    /// <summary>The deity's favored weapon, when it is a simple weapon, has a damage die one step larger.</summary>
    [DataField, AutoNetworkedField]
    public bool DeadlySimplicity;

    /// <summary>In medium or heavy armor, physical damage is reduced by 1 + half the creature's level.</summary>
    [DataField, AutoNetworkedField]
    public bool GuardianArmor;

    /// <summary>Strikes deal 2 extra damage per weapon die with an implement in hand.</summary>
    [DataField, AutoNetworkedField]
    public bool ImplementEmpowerment;

    /// <summary>Any one-handed weapon held counts as this creature's weapon implement.</summary>
    [DataField, AutoNetworkedField]
    public bool WeaponImplement;

    /// <summary>When this creature last cast a spell. Unleash Psyche needs a recent one.</summary>
    [DataField]
    public TimeSpan LastSpellCast;

    /// <summary>Unleash Psyche cannot be used again before this time.</summary>
    [DataField]
    public TimeSpan NextUnleash;

    /// <summary>Overdrive adds 1 more damage.</summary>
    [DataField, AutoNetworkedField]
    public bool ExpertOverdrive;

    /// <summary>A finisher that drops its target gives panache back.</summary>
    [DataField, AutoNetworkedField]
    public bool FinishingFollowThrough;

    /// <summary>A Strike using a stored Devise a Stratagem roll deals extra precision damage.</summary>
    [DataField, AutoNetworkedField]
    public bool StrategicStrike;

    /// <summary>Spells cast from a slot add their rank to damage and healing.</summary>
    [DataField, AutoNetworkedField]
    public bool SorcerousPotency;

    /// <summary>Successful Grapples, Shoves, Disarms and Repositions give panache (Gymnast style).</summary>
    [DataField, AutoNetworkedField]
    public bool PanacheManeuvers;

    /// <summary>Who this creature's reaction marked for extra damage. Only the server reads these.</summary>
    [DataField]
    public EntityUid? MarkedTarget;

    [DataField]
    public int MarkedDamage;

    /// <summary>One die of this size is also added to Strikes against <see cref="MarkedTarget"/>.</summary>
    [DataField]
    public int MarkedDamageDie;

    [DataField]
    public TimeSpan MarkedUntil;

    /// <summary>Walking out of reach needs no action while your shield is raised (Shielded Stride).</summary>
    [DataField, AutoNetworkedField]
    public bool FreeStepWithShield;

    /// <summary>The heal spell rolls d10s instead of d8s.</summary>
    [DataField, AutoNetworkedField]
    public bool HealingHands;

    /// <summary>The harm spell rolls d10s instead of d8s.</summary>
    [DataField, AutoNetworkedField]
    public bool HarmingHands;

    /// <summary>Frightened eases by 2 each turn instead of 1 (Dwarven Doughtiness).</summary>
    [DataField, AutoNetworkedField]
    public bool ShakesOffFear;

    /// <summary>Worn armor does not reduce Speed (Unburdened Iron).</summary>
    [DataField, AutoNetworkedField]
    public bool IgnoresArmorSpeed;

    [DataField, AutoNetworkedField]
    public bool CanRage;

    [DataField, AutoNetworkedField]
    public bool Raging;

    /// <summary>When a Rage next gives temporary Hit Points.</summary>
    [DataField]
    public TimeSpan RageTempHpReadyAt;

    /// <summary>Second Wind: a Rage gives temporary Hit Points without waiting a minute.</summary>
    [DataField, AutoNetworkedField]
    public bool SecondWind;

    /// <summary>What Rage adds to melee damage. Half of it counts with an agile attack.</summary>
    [DataField, AutoNetworkedField]
    public int RageDamage = 2;

    /// <summary>The die of the unarmed attack a rage brings, as Animal instinct's jaws do. 0 means none.</summary>
    [DataField, AutoNetworkedField]
    public int RageAttackDie;

    [DataField, AutoNetworkedField]
    public string RageAttackType = string.Empty;

    /// <summary>
    /// Rage's extra damage is dealt as this type, apart from the weapon's own (a Dragon instinct's breath type, Spirit
    /// instinct's spirit). Empty means it is part of the weapon's damage.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string RageDamageType = string.Empty;

    /// <summary>What Rage adds against a creature that cast a spell in the last hour, in place of <see cref="RageDamage"/> (Superstition). 0 means no change.</summary>
    [DataField, AutoNetworkedField]
    public int RageCasterDamage;

    /// <summary>What Rage adds with an oversized weapon, in place of <see cref="RageDamage"/> (Giant). 0 means no change.</summary>
    [DataField, AutoNetworkedField]
    public int RageOversizedDamage;

    /// <summary>Entering a Rage also restores Hit Points equal to the temporary ones it gives, once per 10 minutes (Superstition).</summary>
    [DataField]
    public bool RageHeals;

    [DataField]
    public TimeSpan RageHealReadyAt;

    /// <summary>Darkvision while raging (Acute Vision).</summary>
    [DataField]
    public bool RageDarkvision;

    /// <summary>Scent while raging (Acute Scent).</summary>
    [DataField]
    public bool RageScent;

    /// <summary>What a Rage switched on, to switch it off again when it ends.</summary>
    [DataField]
    public bool RageGaveVision, RageGaveScent;

    /// <summary>While raging, the flat check against a concealed target is DC 3 and against a hidden one DC 9 (Supernatural Senses).</summary>
    [DataField, AutoNetworkedField]
    public bool RageKeenSenses;

    /// <summary>While raging, the Bulk limits are this much higher (Adrenaline Rush).</summary>
    [DataField, AutoNetworkedField]
    public int RageBulk;

    /// <summary>Thrown weapon Strikes also get the damage from Rage.</summary>
    [DataField, AutoNetworkedField]
    public bool RagingThrower;

    [DataField, AutoNetworkedField]
    public bool ReactiveStrike;

    /// <summary>
    /// Pack Attack, from a stat block: Strikes deal 1 die of this size extra against a creature within reach of at least
    /// two of this creature's allies. 0 is none. <see cref="CFFlankingSystem.AlliesThreatening"/> counts the allies.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int PackAttackDie;

    [DataField, AutoNetworkedField]
    public bool ShieldBlock;

    /// <summary>Dies at dying 5 instead of 4.</summary>
    [DataField, AutoNetworkedField]
    public bool Diehard;

    /// <summary>Bulk limits count as 2 higher.</summary>
    [DataField, AutoNetworkedField]
    public bool HeftyHauler;

    /// <summary>Nine Lives Catfolk: the dying value does not raise the recovery check's DC.</summary>
    [DataField, AutoNetworkedField]
    public bool NineLives;

    /// <summary>The Guardian's Tough to Kill: once a day, the first time dying would reach 3 or more it stays at 2.</summary>
    [DataField, AutoNetworkedField]
    public bool ToughToKill;

    /// <summary>Whether Tough to Kill has been used since the last long rest.</summary>
    [DataField, AutoNetworkedField]
    public bool ToughToKillUsed;

    /// <summary>Recovery checks are 1 easier.</summary>
    [DataField, AutoNetworkedField]
    public bool Toughness;

    [DataField, AutoNetworkedField]
    public bool Flurry;

    [DataField, AutoNetworkedField]
    public bool PowerfulFist;

    [DataField, AutoNetworkedField]
    public bool PanacheStyle;

    [DataField, AutoNetworkedField]
    public bool Panache;

    [DataField, AutoNetworkedField]
    public bool Spellstrike;

    /// <summary>Spellstrike has been used and has to be recharged first.</summary>
    [DataField, AutoNetworkedField]
    public bool SpellstrikeSpent;

    [DataField, AutoNetworkedField]
    public bool Devise;

    [DataField, AutoNetworkedField]
    public bool Exploit;

    [DataField, AutoNetworkedField]
    public bool ReactiveShield;

    [DataField, AutoNetworkedField]
    public bool YoureNext;

    /// <summary>A healer's toolkit used in hand in combat mode heals for one action.</summary>
    [DataField, AutoNetworkedField]
    public bool BattleMedicine;

    /// <summary>A successful Tumble Through leaves the enemy off-guard to the tumbler.</summary>
    [DataField, AutoNetworkedField]
    public bool TumbleBehind;

    /// <summary>Playing a held instrument performs Courageous Anthem.</summary>
    [DataField, AutoNetworkedField]
    public bool Anthem;

    /// <summary>Server-only. When a Bard still playing pays for the next anthem.</summary>
    public TimeSpan NextAnthem;

    /// <summary>A hit with a hand free leaves the target off-guard.</summary>
    [DataField, AutoNetworkedField]
    public bool SnaggingStrike;

    /// <summary>A plain miss with a multiple attack penalty does not add to it.</summary>
    [DataField, AutoNetworkedField]
    public bool ExactingStrike;

    /// <summary>A wide swing at one creature with a weapon in each hand Strikes with both.</summary>
    [DataField, AutoNetworkedField]
    public bool DoubleSlice;

    /// <summary>A wide swing at one creature with a weapon in each hand Strikes with both, and the second finds it off-guard.</summary>
    [DataField, AutoNetworkedField]
    public bool TwinFeint;

    /// <summary>A wide swing at one creature adds a weapon die.</summary>
    [DataField, AutoNetworkedField]
    public bool ViciousSwing;

    /// <summary>The Scoundrel racket's Feint: off-guard for 6 seconds on a success, to everyone on a critical success, and a free Step with an agile or finesse weapon.</summary>
    [DataField, AutoNetworkedField]
    public bool ScoundrelFeint;

    /// <summary>A creature off-guard from this creature's Feint also takes -2 circumstance to Perception and Reflex saves (Distracting Feint).</summary>
    [DataField, AutoNetworkedField]
    public bool DistractingFeint;

    /// <summary>When both Twin Feint Strikes deal damage, the target saves against stupefied 1 (Twin Distraction).</summary>
    [DataField, AutoNetworkedField]
    public bool TwinDistraction;

    /// <summary>A heal spell on one creature also heals another for the spell's rank (Communal Healing).</summary>
    [DataField, AutoNetworkedField]
    public bool CommunalHealing;

    /// <summary>An undead that fails its save against this creature's heal spell is frightened 1 (Panic the Dead).</summary>
    [DataField, AutoNetworkedField]
    public bool PanicTheDead;

    /// <summary>A harm spell that hurts a living creature heals this creature for the spell's rank (Sap Life).</summary>
    [DataField, AutoNetworkedField]
    public bool SapLife;

    /// <summary>Touch of the Void can be cast with 2 actions to enfeeble (Cruelty). The mode menu reads it, so it is networked.</summary>
    [DataField, AutoNetworkedField]
    public bool Cruelty;

    /// <summary>What a 2-action Lay on Hands can end (Mercy). Empty means the character has no Mercy. The mode menu reads it.</summary>
    [DataField, AutoNetworkedField]
    public List<CFConditionKind> MercyConditions = new();

    /// <summary>A Champion's Blessed Shield: 3 more Hardness on a Shield Block, up to 8.</summary>
    [DataField]
    public bool BlessedShield;

    /// <summary>Heal and Harm's area can be a 60-foot cone (Directed Channel). The mode menu reads it, so it is networked.</summary>
    [DataField, AutoNetworkedField]
    public bool DirectedChannel;

    /// <summary>Demoralize uses Performance when it is better than Intimidation (Versatile Performance).</summary>
    [DataField, AutoNetworkedField]
    public bool VersatilePerformance;

    /// <summary>The tick Sap Life last healed on, so one cast heals once.</summary>
    public uint SapLifeTick;

    /// <summary>Divine Infusion: melee Strikes deal this many extra dice of <see cref="InfusionType"/> until <see cref="InfusionUntil"/>.</summary>
    [DataField]
    public int InfusionDice;

    [DataField]
    public int InfusionDie;

    [DataField]
    public string InfusionType = string.Empty;

    [DataField]
    public TimeSpan InfusionUntil;

    /// <summary>What pointing at an enemy does: a Ranger's prey, a Thaumaturge's or Investigator's mark.</summary>
    [DataField, AutoNetworkedField]
    public CFPointMark PointMark;

    /// <summary>Non-zero after this creature has used its reaction. Cleared when the pool refills.</summary>
    [DataField, AutoNetworkedField]
    public int ReactionSpent;

    /// <summary>Server-only. The ranger's hunted creature.</summary>
    public EntityUid? Prey;

    public bool PreyHitThisTurn;

    /// <summary>0 when Devise a Stratagem has not stored a d20.</summary>
    public int StratagemRoll;

    public EntityUid? StratagemTarget;

    public EntityUid? Exploited;

    [DataField, AutoNetworkedField]
    public int NaturalDice;

    [DataField, AutoNetworkedField]
    public int NaturalDie;

    [DataField, AutoNetworkedField]
    public string NaturalType = "";

    [DataField, AutoNetworkedField]
    public bool NaturalFinesse;

    [DataField, AutoNetworkedField]
    public bool NaturalAgile = true;

    /// <summary>The deadly die of the natural attack, or 0.</summary>
    [DataField, AutoNetworkedField]
    public int NaturalDeadly;

    /// <summary>A damage type the natural attack can deal instead (the versatile trait), or empty.</summary>
    [DataField, AutoNetworkedField]
    public string NaturalVersatile = "";

    /// <summary>Strong-Blooded Dwarf: a successful save against a poison lowers its stage by one more.</summary>
    [DataField, AutoNetworkedField]
    public bool StrongBlooded;

    /// <summary>The flat check DC to end persistent fire damage, or 0 for the usual 15 (Charhide Goblin: 10).</summary>
    [DataField, AutoNetworkedField]
    public int PersistentFireDc;

    /// <summary>The flat check DC to end persistent bleed damage, or 0 for the usual 15 (Scar-Thick Skin: 10).</summary>
    [DataField, AutoNetworkedField]
    public int PersistentBleedDc;

    /// <summary>Defy Death: recovery checks are 1 easier.</summary>
    [DataField, AutoNetworkedField]
    public bool DefyDeath;

    /// <summary>A circumstance bonus to Athletics checks to Climb.</summary>
    [DataField, AutoNetworkedField]
    public int ClimbBonus;

    /// <summary>A climb Speed: climbing is a plain move, with no check.</summary>
    [DataField, AutoNetworkedField]
    public bool ClimbSpeed;

    /// <summary>A success on a check to Climb is a critical success.</summary>
    [DataField, AutoNetworkedField]
    public bool ClimbSuccessCritical;

    /// <summary>Venomtail Kobold: until this time, the next Strike that hits with a piercing or slashing weapon poisons.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan TailToxinUntil;

    /// <summary>The persistent poison the venom deals, as dice. 0 dice means the creature's level.</summary>
    [DataField, AutoNetworkedField]
    public int ToxinDice;

    [DataField, AutoNetworkedField]
    public int ToxinDie;

    /// <summary>The venom is on the creature's fangs, so only an unarmed hit delivers it.</summary>
    [DataField, AutoNetworkedField]
    public bool ToxinUnarmedOnly;

    /// <summary>The poison is on a held weapon, so any hit with a weapon delivers it, whatever damage it deals.</summary>
    [DataField]
    public bool ToxinAnyWeapon;

    /// <summary>Spark Fist: until this time a fist hit is lethal, and deals fire damage to the target and to this creature.</summary>
    [DataField]
    public TimeSpan SparkFistUntil;
}

/// <summary>One reaction per full action pool. The value is which reaction was spent.</summary>
public static class CFReactions
{
    public const int Strike = 1;
    public const int Block = 2;
    /// <summary>Any reaction from <see cref="CFReactionSystem"/>.</summary>
    public const int Granted = 4;
    public const int Raise = 8;
    public const int Next = 32;
    /// <summary>Grab an Edge, which every creature knows.</summary>
    public const int Edge = 64;

    public static bool Knows(CFCombatOptionsComponent opt, int bit)
    {
        return bit switch
        {
            Strike => opt.ReactiveStrike,
            Block => opt.ShieldBlock,
            Raise => opt.ReactiveShield,
            Next => opt.YoureNext,
            Edge => true,
            _ => false,
        };
    }
}

/// <summary>The flags on <see cref="CFCombatOptionsComponent"/> that a <c>CFGrantOption</c> can set.</summary>
public enum CFCombatOption : byte
{
    Cruelty,
    BlessedShield,
    SneakAttack,
    CanRage,
    ReactiveStrike,
    ShieldBlock,
    Flurry,
    PowerfulFist,
    PanacheStyle,
    Spellstrike,
    Devise,
    Exploit,
    Anthem,
    BattleMedicine,
    ReactiveShield,
    YoureNext,
    TumbleBehind,
    SnaggingStrike,
    ExactingStrike,
    DoubleSlice,
    TwinFeint,
    ViciousSwing,
    FinesseDexterity,
    ShakesOffFear,
    IgnoresArmorSpeed,
    HealingHands,
    HarmingHands,
    AnyWeaponSneak,
    PreyPrecision,
    PreyFlurry,
    PreyOutwit,
    Bravery,
    DenyAdvantage,
    DreadStriker,
    BrutalBeating,
    UnbalancingBlow,
    FarShot,
    StrongArm,
    FreeStep,
    FreeStepWithShield,
    RagingThrower,
    Diehard,
    HeftyHauler,
    NineLives,
    ToughToKill,
    StrongBlooded,
    EasierBurning,
    EasierBleeding,
    DefyDeath,
    Toughness,
    SecondWind,
    CrushingGrab,
    FinishingFollowThrough,
    StrategicStrike,
    SorcerousPotency,
    PanacheManeuvers,
    SlingerPrecision,
    PunishingShove,
    HumbleStrikes,
    GuardianArmor,
    ImplementEmpowerment,
    WeaponImplement,
    ExpertOverdrive,
    DeadlySimplicity,
    AggressiveBlock,
    GuardedMovement,
    DancingLeaf,
    StunningBlows,
    ScalpelsPoint,
    ElegantBuckler,
    ShieldWarfare,
    QuickTempered,
    SurpriseAttack,
    MastermindRecall,
    MonsterHunter,
    KnownWeaknesses,
    EmotionalPush,
    ThoughtformSummoning,
    ScoundrelFeint,
    DistractingFeint,
    TwinDistraction,
    CommunalHealing,
    PanicTheDead,
    SapLife,
    DirectedChannel,
    VersatilePerformance,
}

public enum CFPointMark : byte
{
    None,
    Prey,
    Exploit,
    Stratagem,
}
