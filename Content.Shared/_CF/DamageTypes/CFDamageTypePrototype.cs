using Content.Shared.Damage.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.DamageTypes;

public enum CFDamageCategory : byte
{
    Physical,
    Energy,
    Other,
}

/// <summary>
/// A Pathfinder 2e Remaster damage type (bludgeoning, fire, void, …).
/// Carpfinder HP is the source of truth; <see cref="Ss14Type"/> is only used when an SS14 object
/// has to take real damage, such as a door or crate broken by a Strike.
/// </summary>
[Prototype("cfDamageType")]
public sealed partial class CFDamageTypePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public CFDamageCategory Category = CFDamageCategory.Other;

    /// <summary>SS14 damage type written to objects that use stock <c>Damageable</c>.</summary>
    [DataField]
    public ProtoId<DamageTypePrototype> Ss14Type = "Blunt";

    /// <summary>Colour of the floating damage number.</summary>
    [DataField]
    public Color Color = Color.FromHex("#FFECA0");

    /// <summary>Default hit sound for weapons of this type that set no <c>soundHit</c>.</summary>
    [DataField]
    public SoundSpecifier? HitSound;

    /// <summary>
    /// Other spellings that resolve to this type, such as SS14 names (<c>slash</c>, <c>heat</c>)
    /// or pre-remaster names (<c>positive</c>, <c>negative</c>).
    /// </summary>
    [DataField]
    public List<string> Aliases = new();
}
