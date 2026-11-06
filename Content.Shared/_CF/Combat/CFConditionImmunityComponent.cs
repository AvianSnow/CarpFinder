namespace Content.Shared.CF.Combat;

/// <summary>
/// Conditions a stat block's immunities rule out. Immunity to a damage type is a <c>CFResistances</c> entry instead.
/// </summary>
[RegisterComponent]
public sealed partial class CFConditionImmunityComponent : Component
{
    [DataField]
    public HashSet<CFConditionKind> Conditions = new();
}
