using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

public enum CFOptionChoiceKind : byte
{
    Cantrip,
    Skill,
    WeaponGroup,
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
}
