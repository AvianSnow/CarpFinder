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
}
