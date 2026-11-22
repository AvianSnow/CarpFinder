using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

[Prototype("cfSkill")]
public sealed partial class CFSkillPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = default!;

    [DataField("description")]
    public string Description { get; private set; } = default!;

    [DataField("ability")]
    public CFAbility Ability { get; private set; } = default!;

    [DataField("skillRanks")]
    public Dictionary<string, CFSkillRank> SkillRanks { get; private set; } = new();
}
