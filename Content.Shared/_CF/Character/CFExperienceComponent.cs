using Content.Shared.CF.Preferences;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.Character;

/// <summary>
/// Progress toward the next level, plus the feats chosen in the lobby for levels 1 through 5.
/// Medium advancement: 1000 XP levels you up and the remainder carries over.
/// A character spawns at level 1. Later feats wait until that level.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFExperienceComponent : Component
{
    public const int XpPerLevel = 1000;
    public const int MaxLevel = 5;

    [DataField, AutoNetworkedField]
    public int Xp;

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
