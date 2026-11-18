using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

public enum CFOptionChoiceKind : byte
{
    Cantrip,
    Skill,
    WeaponGroup,
    Language,

    /// <summary>One of <see cref="CFOptionChoice.Abilities"/>, as the class's key attribute.</summary>
    KeyAttribute,

    /// <summary>A saving throw or Perception, raised to expert.</summary>
    SaveOrPerception,

    /// <summary>One of the build's spells of <see cref="CFOptionChoice.SpellRank"/>, which can then be cast at any rank.</summary>
    SignatureSpell,

    /// <summary>A feat of the player's choice, from the slot <see cref="CFOptionChoice.FeatSlot"/> takes, of <see cref="CFOptionChoice.FeatLevel"/> or lower.</summary>
    Feat,

    /// <summary>One of <see cref="CFOptionChoice.DamageTypes"/>, read by a grant such as <c>CFGrantChosenResistance</c>.</summary>
    DamageType,

    /// <summary>A favored terrain (<c>CFFavoredTerrain</c>), read by <c>CFGrantFavoredTerrain</c>.</summary>
    Terrain,

    /// <summary>
    /// One of the deity's domains (<c>cfDomain</c>), read by <c>CFGrantDomainSpell</c>. Splinter Faith's picks replace the
    /// deity's, and <see cref="CFOptionChoice.AnyDomain"/> offers them all.
    /// </summary>
    Domain,
}

/// <summary>
/// Something the player picks when taking an option: a cantrip or a skill. Written under <c>choices</c> on a feat, heritage or
/// subclass option. The builder asks for it, and <see cref="CFOptionChoices"/> checks the answer.
/// </summary>
[DataDefinition]
public sealed partial class CFOptionChoice
{
    [DataField(required: true)]
    public CFOptionChoiceKind Kind;

    /// <summary>How many different picks.</summary>
    [DataField]
    public int Count = 1;

    /// <summary>
    /// The pick can be changed every day: the player plans a new one on the character sheet's Daily tab, and the next long
    /// rest swaps it in. Only a skill, a cantrip and a signature spell can be daily, because those can be taken back.
    /// </summary>
    [DataField]
    public bool Daily;

    /// <summary>A cantrip must be on one of these spell lists. Empty means any.</summary>
    [DataField]
    public List<CFSpellTradition> Traditions = new();

    /// <summary>A skill must be one of these. Empty means any.</summary>
    [DataField]
    public List<ProtoId<CFAbilitySkillPrototype>> Skills = new();

    /// <summary>What a <see cref="CFOptionChoiceKind.KeyAttribute"/> choice can be answered with.</summary>
    [DataField]
    public List<CFAbility> Abilities = new();

    /// <summary>The rank a <see cref="CFOptionChoiceKind.SignatureSpell"/> choice picks from.</summary>
    [DataField]
    public int SpellRank;

    /// <summary>What kind of feat a <see cref="CFOptionChoiceKind.Feat"/> choice offers: a general, skill, class or ancestry feat.</summary>
    [DataField]
    public CFLevelSlot FeatSlot = CFLevelSlot.GeneralFeat;

    /// <summary>The highest feat level a <see cref="CFOptionChoiceKind.Feat"/> choice offers.</summary>
    [DataField]
    public int FeatLevel = 1;

    /// <summary>A <see cref="CFOptionChoiceKind.Domain"/> choice offers every domain, not only the deity's (Splinter Faith).</summary>
    [DataField]
    public bool AnyDomain;

    /// <summary>What a <see cref="CFOptionChoiceKind.DamageType"/> choice can be answered with.</summary>
    [DataField]
    public List<ProtoId<CFDamageTypePrototype>> DamageTypes = new();
}
