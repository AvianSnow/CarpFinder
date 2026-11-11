using Content.Shared.CF.Preferences;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.Character;

/// <summary>
/// Progress toward the next level, and the picks for each level. Medium advancement: 1000 XP earns a level and the rest
/// carries over. A character spawns at level 1 with what the lobby built. When it has the XP, the player picks the next
/// level on the character sheet and accepts it (<see cref="CFLevelUp"/>).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFExperienceComponent : Component
{
    public const int XpPerLevel = 1000;
    public const int MaxLevel = 5;

    [DataField, AutoNetworkedField]
    public int Xp;

    /// <summary>
    /// The character's build, checked at spawn, with each level's picks added as it is taken. The character sheet's
    /// Level Up tab edits a copy of it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public CFCharacterBuildData? Build;

    /// <summary>The ancestry the build was made for.</summary>
    [DataField, AutoNetworkedField]
    public string? AncestryId;

    /// <summary>True when the character has the XP for another level and has not taken it yet.</summary>
    public bool CanLevelUp(int level)
    {
        return level < MaxLevel && Xp >= XpPerLevel;
    }

    [DataField, AutoNetworkedField]
    public string? AncestryFeat;

    [DataField, AutoNetworkedField]
    public string? ClassFeat;

    [DataField, AutoNetworkedField]
    public string? ClassFeatLevel2;

    [DataField, AutoNetworkedField]
    public string? ClassFeatLevel4;

    [DataField, AutoNetworkedField]
    public string? AncestryFeatLevel5;

    [DataField, AutoNetworkedField]
    public string? SkillFeatLevel2;

    [DataField, AutoNetworkedField]
    public string? GeneralFeatLevel3;

    [DataField, AutoNetworkedField]
    public string? SkillFeatLevel4;

    [DataField, AutoNetworkedField]
    public string? SkillIncreaseLevel3;

    [DataField, AutoNetworkedField]
    public string? SkillIncreaseLevel5;

    /// <summary>The language a level 5 Intelligence boost teaches, already checked at spawn.</summary>
    [DataField]
    public string? LanguageLevel5;

    /// <summary>Skill feats a class gives on top of the usual ones, by level. Level 1's is applied at spawn.</summary>
    [DataField]
    public Dictionary<int, string> BonusSkillFeats = new();

    /// <summary>Skill increases a class gives on top of the usual ones, by level.</summary>
    [DataField]
    public Dictionary<int, string> BonusSkillIncreases = new();

    [DataField, AutoNetworkedField]
    public List<CFAbility> AbilityBoostsLevel5 = new();

    /// <summary>Cantrips and skills chosen for options, checked at spawn. Each is applied when its option is gained.</summary>
    [DataField, AutoNetworkedField]
    public List<CFOptionPick> OptionPicks = new();

    /// <summary>Subclass picks, checked at spawn. Their later features run on level up.</summary>
    [DataField, AutoNetworkedField]
    public List<string> ClassChoices = new();

    /// <summary>The spells picked in the lobby, for the slots that arrive at later levels.</summary>
    [DataField]
    public List<CFSpellPick> SpellPicks = new();
}
