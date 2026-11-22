namespace Content.Shared.CF.Combat;

/// <summary>
/// Void healing, as undead have. Vitality healing does nothing to this creature. That it takes no void damage is a
/// <c>CFResistances</c> entry on the creature.
/// </summary>
[RegisterComponent]
public sealed partial class CFVoidHealingComponent : Component
{
}
