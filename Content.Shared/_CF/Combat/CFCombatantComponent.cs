using Robust.Shared.GameStates;

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

    /// <summary>
    /// Extra trained skills from background picks and free class skill picks
    /// (class fixed skills stay on the class proficiency list).
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> TrainedSkills = new();

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
