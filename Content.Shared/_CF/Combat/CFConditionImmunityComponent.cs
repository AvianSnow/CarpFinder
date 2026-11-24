using Content.Shared.CF.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// What a stat block's immunities rule out, other than damage types: immunity to a damage type is a
/// <c>CFResistances</c> entry instead. Read through <see cref="CFConditionSystem.IsImmune"/>,
/// <see cref="CFConditionSystem.IsImmuneToTraits"/>, <see cref="CFConditionSystem.ImmuneToCriticalHits"/> and
/// <see cref="CFConditionSystem.ImmuneToPrecision"/>.
/// </summary>
[RegisterComponent]
public sealed partial class CFConditionImmunityComponent : Component
{
    [DataField]
    public HashSet<CFConditionKind> Conditions = new();

    /// <summary>
    /// Effects with any of these traits do nothing to the creature: Mental, Poison, Disease, Death, Sleep, Visual
    /// and so on. An affliction with one never takes hold.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<CFTraitPrototype>> Traits = new();

    /// <summary>A critical hit deals the damage of a hit. Whatever else the critical hit does still happens.</summary>
    [DataField]
    public bool CriticalHits;

    /// <summary>Precision damage, such as a sneak attack's, is not dealt.</summary>
    [DataField]
    public bool Precision;
}
