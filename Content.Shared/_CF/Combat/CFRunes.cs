using Content.Shared.CF.Character;
using Content.Shared.CF.Effects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Fundamental and property runes etched on a weapon.
/// Potency is an item bonus to attack rolls; striking adds weapon damage dice.
/// A weapon holds as many property runes as its potency.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFWeaponRunesComponent : Component
{
    /// <summary>+1 to +3.</summary>
    [DataField, AutoNetworkedField]
    public int Potency;

    /// <summary>1 striking, 2 greater striking, 3 major striking: that many extra weapon damage dice.</summary>
    [DataField, AutoNetworkedField]
    public int Striking;

    [DataField, AutoNetworkedField]
    public List<ProtoId<CFPropertyRunePrototype>> Property = new();
}

/// <summary>
/// Fundamental runes etched on armor. Potency is an item bonus to AC; resilient is an item bonus to saves.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFArmorRunesComponent : Component
{
    /// <summary>+1 to +3 item bonus to AC.</summary>
    [DataField, AutoNetworkedField]
    public int Potency;

    /// <summary>1 resilient, 2 greater, 3 major: +1 to +3 item bonus to saving throws.</summary>
    [DataField, AutoNetworkedField]
    public int Resilient;
}

/// <summary>
/// A weapon property rune. Its effects run on the target after a Strike with the weapon hits,
/// with the Strike's degree, so a <c>CFDamageEffect</c> here doubles on a critical hit.
/// </summary>
[Prototype("cfPropertyRune")]
public sealed partial class CFPropertyRunePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name { get; private set; } = "";

    [DataField]
    public int Level;

    [DataField]
    public CFRarity Rarity = CFRarity.Common;

    /// <summary>Price in copper pieces.</summary>
    [DataField]
    public int Price;

    [DataField]
    public string Summary { get; private set; } = "";

    /// <summary>Runs on every hit, critical or not.</summary>
    [DataField]
    public List<CFEffect> OnHit = new();

    /// <summary>Runs on a critical hit, after <see cref="OnHit"/>.</summary>
    [DataField]
    public List<CFEffect> OnCriticalHit = new();
}
