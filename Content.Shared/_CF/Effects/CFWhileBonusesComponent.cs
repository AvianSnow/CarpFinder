using Content.Shared.CF.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>A state a bonus can depend on.</summary>
public enum CFBonusWhile : byte
{
    Raging,
    Panache,
    Unarmored,
    HoldingRegalia,
    HoldingLantern,
    HasBanner,
}

/// <summary>A lasting bonus that only counts while the creature is in a state. See <see cref="CFWhileBonusSystem"/>.</summary>
[DataDefinition]
public sealed partial class CFWhileBonus
{
    [DataField(required: true)]
    public CFBonusWhile While;

    [DataField(required: true)]
    public CFBonusStat Stat;

    [DataField(required: true)]
    public CFBonusType Type;

    /// <summary>Negative for a penalty.</summary>
    [DataField(required: true)]
    public int Value;

    /// <summary>When set, the bonus only counts against a source with this trait.</summary>
    [DataField]
    public ProtoId<CFTraitPrototype>? Trait;

    /// <summary>When set with <see cref="CFBonusStat.SkillChecks"/>, the bonus only counts for this skill.</summary>
    [DataField]
    public string? Skill;
}

/// <summary>What a creature gains in certain states, such as while raging, and which of those states are in effect now.</summary>
[RegisterComponent]
public sealed partial class CFWhileBonusesComponent : Component
{
    [DataField]
    public List<CFWhileBonus> Bonuses = new();

    /// <summary>The states whose bonuses are on the creature right now.</summary>
    [DataField]
    public HashSet<CFBonusWhile> Active = new();

    [DataField]
    public TimeSpan NextCheck;
}
