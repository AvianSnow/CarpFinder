using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Companions;

public enum CFPetKind
{
    Companion,
    Familiar,

    /// <summary>An Inventor's construct companion.</summary>
    Construct,
}

/// <summary>
/// A young animal companion or a familiar body for the character builder.
/// A companion's level matches its master. It is trained in unarmed attacks, unarmored defense, barding,
/// saving throws, Perception, Acrobatics, and Athletics, plus the skill on its type.
/// Hit Points are the type's ancestry Hit Points plus 6 + Constitution for each of the master's levels.
/// </summary>
[Prototype("cfPet")]
public sealed partial class CFPetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = "";

    [DataField]
    public CFPetKind Kind = CFPetKind.Companion;

    [DataField]
    public string Description = "";

    [DataField]
    public string Size = "Small";

    [DataField]
    public int Strength = 2;

    [DataField]
    public int Dexterity = 2;

    [DataField]
    public int Constitution = 2;

    [DataField]
    public int Intelligence = -4;

    [DataField]
    public int Wisdom = 2;

    [DataField]
    public int Charisma;

    /// <summary>Ancestry Hit Points printed on the companion type. Familiars store their flat total here instead.</summary>
    [DataField]
    public int HitPoints = 6;

    /// <summary>Add ancestry Hit Points to 6 + Constitution for every level. Familiars keep the flat total.</summary>
    [DataField]
    public bool ScaleHitPoints = true;

    /// <summary>The companion is suited to riding and ignores the usual mount limits.</summary>
    [DataField]
    public bool Mount;

    /// <summary>Spawned body. Parents <c>CFCompanionBase</c>.</summary>
    [DataField]
    public string Entity = "";

    /// <summary>Armor Class, saves, and Perception use the summoner's sheet.</summary>
    [DataField]
    public bool UsesMaster;

    [DataField]
    public List<CFPetSpeed> Speeds = new();

    [DataField]
    public List<string> Senses = new();

    [DataField]
    public List<CFPetAttack> Attacks = new();

    [DataField]
    public List<CFPetSkill> Skills = new();

    [DataField]
    public string Support = "";

    [DataField]
    public string Advanced = "";
}

[DataDefinition]
public sealed partial class CFPetSpeed
{
    [DataField(required: true)]
    public string Name = "";

    [DataField(required: true)]
    public int Feet;
}

[DataDefinition]
public sealed partial class CFPetAttack
{
    [DataField(required: true)]
    public string Name = "";

    [DataField]
    public int Dice = 1;

    [DataField]
    public int Die = 6;

    [DataField]
    public string Damage = "bludgeoning";

    [DataField]
    public bool Agile;

    [DataField]
    public bool Finesse;

    /// <summary>Traits other than agile and finesse, such as reach or shove.</summary>
    [DataField]
    public List<string> Traits = new();
}

[DataDefinition]
public sealed partial class CFPetSkill
{
    [DataField(required: true)]
    public string Name = "";

    [DataField(required: true)]
    public CFAbility Ability;
}

/// <summary>A piece of gear a companion can carry. The equipment popup lists these.</summary>
[Prototype("cfPetGear")]
public sealed partial class CFPetGearPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = "";

    [DataField]
    public string Summary = "";

    /// <summary>Item spawned with the companion when this gear is chosen.</summary>
    [DataField]
    public string Entity = "";
}
