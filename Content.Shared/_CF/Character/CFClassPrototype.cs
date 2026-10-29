using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

[Prototype("cfClass")]
public sealed partial class CFClassPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField(required: true)]
    public int HitPoints;

    [DataField(required: true)]
    public CFAbility KeyAbility;

    [DataField(required: true)]
    public CFAbility HitPointsAbility;

    [DataField(required: true)]
    public LocId ClassDC;

    [DataField]
    public CFClassProficiencies Proficiencies = new();

    [DataField]
    public List<CFAbilityBoost> AbilityBoosts = new();
}

[DataDefinition]
public sealed partial class CFClassProficiencies
{
    [DataField]
    public CFProficiencyList Perception = new();

    [DataField]
    public CFProficiencyList SavingThrows = new();

    [DataField]
    public CFProficiencyList Skills = new();

    [DataField]
    public CFProficiencyList Attacks = new();

    [DataField]
    public CFProficiencyList Defenses = new();
}

[DataDefinition]
public sealed partial class CFProficiencyList
{
    [DataField]
    public List<string> Trained = new();

    [DataField]
    public List<string> Expert = new();

    [DataField]
    public List<string> Master = new();

    [DataField]
    public List<string> Legendary = new();
}
