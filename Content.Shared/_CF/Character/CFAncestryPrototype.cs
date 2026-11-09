using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Languages;
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
/// Carpfinder ancestry rules for a playable species.
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

    /// <summary>Extra bodies the player can pick for this ancestry. The default body is <see cref="Species"/>.</summary>
    [DataField]
    public List<CFAncestrySprite> Sprites = new();

    public bool OwnsSpecies(string speciesId)
    {
        if (Species.Id == speciesId)
            return true;

        foreach (var sprite in Sprites)
        {
            if (sprite.Species.Id == speciesId)
                return true;
        }

        return false;
    }

    [DataField]
    public CFRarity Rarity = CFRarity.Common;

    [DataField]
    public int HitPoints = 8;

    [DataField]
    public CFSize Size = CFSize.Medium;

    [DataField]
    public int Speed = 25;

    /// <summary>Darkvision. Applied as night vision when the character spawns. Low-light vision is a listed trait only.</summary>
    [DataField]
    public bool Darkvision;

    [DataField]
    public List<CFAbilityBoost> AbilityBoosts = new();

    [DataField]
    public List<CFAbility> AbilityFlaws = new();

    [DataField]
    public List<ProtoId<CFLanguagePrototype>> Languages = new();

    [DataField]
    public List<LocId> Traits = new();

    /// <summary>What this gives a character. See <see cref="CFGrant"/>.</summary>
    [DataField]
    public List<CFGrant> Grants = new();
}

/// <summary>A body sprite a player can use for an ancestry.</summary>
[DataDefinition]
public sealed partial class CFAncestrySprite
{
    [DataField(required: true)]
    public ProtoId<SpeciesPrototype> Species;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>Parts RSI whose meta.json license and copyright are shown in the sprite picker.</summary>
    [DataField]
    public string? Parts;

    /// <summary>When set, the sprite is listed only while the character has this heritage.</summary>
    [DataField]
    public ProtoId<CFHeritagePrototype>? Heritage;
}
