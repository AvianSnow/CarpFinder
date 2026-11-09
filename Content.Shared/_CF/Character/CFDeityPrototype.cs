using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

/// <summary>A deity a Cleric or Champion follows. It is data on the character, not a religion system.</summary>
[Prototype("cfDeity")]
public sealed partial class CFDeityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField]
    public CFRarity Rarity = CFRarity.Common;

    /// <summary>The deity's divine skill.</summary>
    [DataField]
    public List<ProtoId<CFAbilitySkillPrototype>> TrainedSkills = new();

    /// <summary>What following this deity gives, such as familiarity with its favored weapon.</summary>
    [DataField]
    public List<CFGrant> Grants = new();
}
