using Content.Shared.CF.DamageTypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Remaster weapon stats shared by melee and ranged weapons, and by a creature's own natural weapon.
/// Add <see cref="CFMeleeWeaponComponent"/> or <see cref="CFRangedWeaponComponent"/> for how it is used.
/// Traits (agile, deadly d10, versatile P, …) go on <c>CFTrait</c> on the same entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFWeaponComponent : Component
{
    [DataField, AutoNetworkedField]
    public int DamageDice = 1;

    [DataField, AutoNetworkedField]
    public int DamageDie = 4;

    [DataField(required: true), AutoNetworkedField]
    public ProtoId<CFDamageTypePrototype> DamageType = CFDamageTypes.Bludgeoning;

    [DataField, AutoNetworkedField]
    public CFWeaponCategory Category = CFWeaponCategory.Simple;

    [DataField, AutoNetworkedField]
    public CFWeaponGroup Group = CFWeaponGroup.Club;

    [DataField, AutoNetworkedField]
    public CFWeaponHands Hands = CFWeaponHands.One;
}

public enum CFWeaponCategory : byte
{
    Unarmed,
    Simple,
    Martial,
    Advanced,
}

/// <summary>Weapon groups. Critical specialization effects key off these (later, with class features).</summary>
public enum CFWeaponGroup : byte
{
    Axe,
    Bomb,
    Bow,
    Brawling,
    Club,
    Crossbow,
    Dart,
    Firearm,
    Flail,
    Hammer,
    Knife,
    Pick,
    Polearm,
    Shield,
    Sling,
    Spear,
    Sword,
}

public enum CFWeaponHands : byte
{
    One,

    /// <summary>"1+": held in one hand, but a second hand is needed to load or use it (crossbows).</summary>
    OnePlus,
    Two,
}

public static class CFWeaponCategories
{
    /// <summary>The class proficiency list name (<c>attacks:</c> in class YAML) for a category.</summary>
    public static string Proficiency(this CFWeaponCategory category)
    {
        return category switch
        {
            CFWeaponCategory.Unarmed => "Unarmed",
            CFWeaponCategory.Simple => "SimpleWeapons",
            CFWeaponCategory.Martial => "MartialWeapons",
            _ => "AdvancedWeapons",
        };
    }
}
