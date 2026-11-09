using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

/// <summary>
/// One option of a subclass: a Racket, Instinct, Muse, Doctrine, and so on. The builder makes one button for each
/// <see cref="Slot"/> a class has options in, so adding options is all a class needs.
/// </summary>
[Prototype("cfClassChoice")]
public sealed partial class CFClassChoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<CFClassPrototype> Class;

    /// <summary>What is being chosen, such as "Rogue's Racket". A character has one option per slot.</summary>
    [DataField(required: true)]
    public LocId Slot;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField]
    public CFRarity Rarity = CFRarity.Common;

    /// <summary>Replaces the class's key attribute, for its boost and class DC.</summary>
    [DataField]
    public CFAbility? KeyAbility;

    /// <summary>Replaces the class's spell tradition, as a Sorcerer's bloodline does.</summary>
    [DataField]
    public CFSpellTradition? Tradition;

    /// <summary>
    /// Skills this option trains. Kept apart from <see cref="Grants"/> so the builder can see them and not offer them as
    /// class skill picks.
    /// </summary>
    [DataField]
    public List<ProtoId<CFAbilitySkillPrototype>> TrainedSkills = new();

    /// <summary>What the player picks when taking this.</summary>
    [DataField]
    public List<CFOptionChoice> Choices = new();

    /// <summary>What this gives at level 1. See <see cref="CFGrant"/>.</summary>
    [DataField]
    public List<CFGrant> Grants = new();

    /// <summary>What this gives at later levels, such as a Doctrine's level 3 benefit.</summary>
    [DataField]
    public List<CFClassFeature> Features = new();
}
