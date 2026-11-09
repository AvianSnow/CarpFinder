using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

public enum CFAuraAffects : byte
{
    Allies,
    Enemies,
}

public enum CFAuraNeeds : byte
{
    Nothing,

    /// <summary>The owner cast Shields of the Spirit and still has the shield raised.</summary>
    SpiritShields,

    HoldingRegalia,
    HoldingLantern,
    HasBanner,

    /// <summary>The ikon named by <see cref="CFAuraEffect.Ikon"/> holds the owner's divine spark.</summary>
    Immanence,
}

/// <summary>One thing an aura does to the creatures inside it. See <see cref="CFAuraSystem"/>.</summary>
[DataDefinition]
public sealed partial class CFAuraEffect
{
    [DataField(required: true)]
    public CFAuraAffects Affects;

    [DataField]
    public CFAuraNeeds Needs;

    /// <summary>For <see cref="CFAuraNeeds.Immanence"/>.</summary>
    [DataField]
    public string Ikon = string.Empty;

    /// <summary>The stat of a bonus that lasts while a creature is inside. Null means no bonus.</summary>
    [DataField]
    public CFBonusStat? Stat;

    [DataField]
    public CFBonusType Type;

    /// <summary>Negative for a penalty.</summary>
    [DataField]
    public int Value;

    /// <summary>When set, the bonus only counts against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>Every 6 seconds, those inside lose 1 from frightened.</summary>
    [DataField]
    public bool EasesFrightened;

    /// <summary>Those inside cannot get frightened down below 1 by themselves.</summary>
    [DataField]
    public bool KeepsFrightened;

    /// <summary>The die of the damage an enemy takes each time it attacks someone inside. 0 means none.</summary>
    [DataField]
    public int RetaliateDie;

    [DataField]
    public ProtoId<CFDamageTypePrototype>? RetaliateType;
}

/// <summary>
/// An emanation that follows a creature. Everything it does is in <see cref="Effects"/>, which <c>CFGrantAura</c> adds to.
/// </summary>
[RegisterComponent]
public sealed partial class CFAuraComponent : Component
{
    [DataField]
    public int SizeFeet = 15;

    [DataField]
    public List<CFAuraEffect> Effects = new();

    /// <summary>Who each entry of <see cref="Effects"/> is on right now, by index.</summary>
    public Dictionary<int, HashSet<EntityUid>> Inside = new();

    [DataField]
    public TimeSpan NextCheck;

    [DataField]
    public TimeSpan NextEase;

    /// <summary>Shields of the Spirit lasts until this time.</summary>
    [DataField]
    public TimeSpan SpiritShieldsUntil;
}

/// <summary>On a creature inside an aura that hurts whoever attacks it.</summary>
[RegisterComponent]
public sealed partial class CFAuraRetaliationComponent : Component
{
    [DataField]
    public EntityUid Owner;

    [DataField]
    public int Die;

    [DataField]
    public string DamageType = string.Empty;
}

/// <summary>An action that cannot be used without a shield in hand.</summary>
[RegisterComponent]
public sealed partial class CFNeedsShieldComponent : Component
{
}
