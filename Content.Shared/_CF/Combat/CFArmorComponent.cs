using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Worn remaster armor. Applies only while worn in an inventory slot, never while held.
/// Traits (bulwark, comfort, flexible, noisy) go on <c>CFTrait</c> on the same entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFArmorComponent : Component
{
    [DataField, AutoNetworkedField]
    public CFArmorCategory Category = CFArmorCategory.Light;

    [DataField, AutoNetworkedField]
    public CFArmorGroup Group = CFArmorGroup.Leather;

    /// <summary>Armor's bonus to AC (leather +1, hide +3, full plate +6).</summary>
    [DataField, AutoNetworkedField]
    public int AcBonus = 1;

    /// <summary>Highest Dexterity modifier that still adds to AC. Null is no cap.</summary>
    [DataField, AutoNetworkedField]
    public int? DexCap = 4;

    /// <summary>Penalty to Strength- and Dexterity-based skill checks without the attack trait, as printed (−1).</summary>
    [DataField, AutoNetworkedField]
    public int CheckPenalty;

    /// <summary>Speed penalty in feet, as printed (−5).</summary>
    [DataField, AutoNetworkedField]
    public int SpeedPenalty;

    /// <summary>
    /// Strength modifier that removes the check penalty and lowers the Speed penalty by 5 feet. Null is none.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int? Strength;

    /// <summary>The class proficiency list name (<c>defenses:</c> in class YAML) for this armor.</summary>
    public string Proficiency => Category switch
    {
        CFArmorCategory.Unarmored => "Unarmored",
        CFArmorCategory.Light => "LightArmor",
        CFArmorCategory.Medium => "MediumArmor",
        _ => "HeavyArmor",
    };
}

public enum CFArmorCategory : byte
{
    Unarmored,
    Light,
    Medium,
    Heavy,
}

/// <summary>Armor groups. Armor specialization effects key off these (later, with class features).</summary>
public enum CFArmorGroup : byte
{
    Cloth,
    Leather,
    Composite,
    Chain,
    Plate,
    Skeletal,
    Wood,
}
