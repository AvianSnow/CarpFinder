using Content.Shared.CF.DamageTypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Alchemy;

/// <summary>
/// An alchemical bomb: a thrown weapon (<c>CFWeapon</c> with the thrown trait) that breaks on its Strike. On a
/// failure, a success or a critical success every creature within 5 feet of the target takes <see cref="Splash"/>
/// damage of the bomb's type; on a hit the target also takes <see cref="Persistent"/> persistent damage and is
/// off-guard for <see cref="OffGuardSeconds"/>. A critical failure, or a throw that hits nobody, only wastes it.
/// </summary>
[RegisterComponent]
public sealed partial class CFBombComponent : Component
{
    /// <summary>Splash damage, of the weapon's damage type.</summary>
    [DataField]
    public int Splash = 1;

    /// <summary>Flat persistent damage on a hit.</summary>
    [DataField]
    public int Persistent;

    /// <summary>The persistent damage's type. The weapon's own when unset.</summary>
    [DataField]
    public ProtoId<CFDamageTypePrototype>? PersistentType;

    /// <summary>On a hit, the target is off-guard this long (bottled lightning).</summary>
    [DataField]
    public float OffGuardSeconds;

    [DataField]
    public SoundSpecifier BreakSound = new SoundCollectionSpecifier("GlassBreak");

    /// <summary>Already broken this tick: a throw can both hit and land.</summary>
    public bool Broken;
}
