using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

[Prototype("cfBackground")]
public sealed partial class CFBackgroundPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = default!;

    [DataField("description")]
    public string Description { get; private set; } = default!;

    [DataField("skillRanks")]
    public Dictionary<string, CFSkillRank> SkillRanks { get; private set; } = new();

    [DataField("abilityScoreBonuses")]
    public Dictionary<CFAbility, int> AbilityScoreBonuses { get; private set; } = new()
    {
        { CFAbility.Strength, 0 },
        { CFAbility.Dexterity, 0 },
        { CFAbility.Constitution, 0 },
        { CFAbility.Intelligence, 0 },
        { CFAbility.Wisdom, 0 },
        { CFAbility.Charisma, 0 }
    };
}
