using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Carpfinder combat stats (AC / HP) for player characters and mobs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class CFCombatantComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Level = 1;

    [DataField, AutoNetworkedField]
    public int ArmorClass = 10;

    /// <summary>
    /// True for characters: AC is recomputed from worn armor whenever it changes.
    /// False for creatures with a printed AC in their stat block (monsters, commoners), which keep it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ArmorFromEquipment;

    /// <summary>
    /// Land Speed in feet (25 for most ancestries). Armor and encumbrance penalties scale SS14 movement from this.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int SpeedFeet = 25;

    /// <summary>
    /// Added to every Strike so a creature matches its printed stat block attack (a level −1 kobold's spear is +3).
    /// Player characters leave this 0: their attacks come from abilities, proficiency, and runes.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int StrikeAdjustment;

    /// <summary>
    /// Seconds an NPC takes to think before each attack on a creature. A clever or quick creature, such as a boss,
    /// sets this lower. 0 means it never hesitates. It does not apply while a player is in the body.
    /// </summary>
    [DataField]
    public float ThinkingTimer = 1f;

    /// <summary>Stat block Fortitude. Null means work it out from the class, or Commoner stats without one.</summary>
    [DataField, AutoNetworkedField]
    public int? PrintedFortitude;

    /// <summary>Stat block Reflex.</summary>
    [DataField, AutoNetworkedField]
    public int? PrintedReflex;

    /// <summary>Stat block Will.</summary>
    [DataField, AutoNetworkedField]
    public int? PrintedWill;

    /// <summary>Stat block Perception.</summary>
    [DataField, AutoNetworkedField]
    public int? PrintedPerception;

    /// <summary>A stat block's melee attack bonus. It replaces abilities and proficiency.</summary>
    [DataField, AutoNetworkedField]
    public int? PrintedAttack;

    /// <summary>The flat part of a stat block's melee damage, where it differs from Strength.</summary>
    [DataField, AutoNetworkedField]
    public int? PrintedDamage;

    /// <summary>Stat block skill modifiers by skill id, where the book differs from abilities and proficiency.</summary>
    [DataField]
    public Dictionary<string, int> PrintedSkills = new();

    /// <summary>Monsters die at 0 HP instead of dying, and defeating one is worth XP.</summary>
    [DataField, AutoNetworkedField]
    public bool DiesAtZeroHp;

    [DataField, AutoNetworkedField]
    public int MaxHp = 10;

    [DataField, AutoNetworkedField]
    public int CurrentHp = 10;

    [DataField, AutoNetworkedField]
    public string? ClassId;

    [DataField, AutoNetworkedField]
    public string? BackgroundId;

    /// <summary>The key attribute when something other than the class sets it, such as a Rogue's racket.</summary>
    [DataField, AutoNetworkedField]
    public CFAbility? KeyAbility;

    /// <summary>
    /// Extra trained skills from background picks and free class skill picks
    /// (class fixed skills stay on the class proficiency list).
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> TrainedSkills = new();

    /// <summary>Extra Hit Points at each level, from Toughness and the like. Level-up adds this.</summary>
    [DataField, AutoNetworkedField]
    public int BonusHpPerLevel;

    /// <summary>Skills raised by a skill increase, once for each increase.</summary>
    [DataField, AutoNetworkedField]
    public List<string> SkillIncreases = new();

    /// <summary>
    /// Ranks raised by feats and class features, by proficiency id (<c>Fortitude</c>, <c>MartialWeapons</c>, <c>Sword</c>).
    /// A rank here only counts when it beats the class's own. Read through <c>CFDefense</c>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, CFProficiencyRank> ProficiencyRanks = new();

    /// <summary>Weapons that count as simple for this character, who is at least trained with them.</summary>
    [DataField, AutoNetworkedField]
    public List<EntProtoId> FamiliarWeapons = new();

    /// <summary>Weapons with these traits count as one category simpler for this character.</summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<CFTraitPrototype>> FamiliarWeaponTraits = new();

    /// <summary>Specific Lore names from the chosen ancestry feat, shown on the Lore row.</summary>
    [DataField, AutoNetworkedField]
    public List<string> LoreNames = new();

    [DataField, AutoNetworkedField]
    public int TemporaryHp;

    [DataField, AutoNetworkedField]
    public int Dying;

    [DataField, AutoNetworkedField]
    public int Wounded;
}
