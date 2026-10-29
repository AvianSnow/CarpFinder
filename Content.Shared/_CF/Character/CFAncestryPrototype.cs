using Content.Shared.CF.Preferences;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

public enum CFRarity
{
    Common,
    Uncommon,
    Rare,
}

public enum CFSize
{
    Tiny,
    Small,
    Medium,
    Large,
}

/// <summary>
/// Pathfinder ancestry rules for a playable species.
/// </summary>
[Prototype("cfAncestry")]
public sealed partial class CFAncestryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField(required: true)]
    public ProtoId<SpeciesPrototype> Species;

    [DataField]
    public CFRarity Rarity = CFRarity.Common;

    [DataField]
    public int HitPoints = 8;

    [DataField]
    public CFSize Size = CFSize.Medium;

    [DataField]
    public int Speed = 25;

    [DataField]
    public List<CFAbilityBoost> AbilityBoosts = new();

    [DataField]
    public List<CFAbility> AbilityFlaws = new();

    [DataField]
    public List<LocId> Languages = new();

    [DataField]
    public List<LocId> Traits = new();
}
