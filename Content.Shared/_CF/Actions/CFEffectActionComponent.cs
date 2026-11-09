namespace Content.Shared.CF.Actions;

/// <summary>
/// An action that only applies effects: <c>CFDegreeEffects</c>, and <c>CFCheck</c> if it rolls one. Parent an action on
/// <c>CFActivityInstant</c> for one on yourself or <c>CFActivityTarget</c> for one on a creature, and grant it with
/// <c>CFGrantAction</c>. <see cref="CFEffectActionSystem"/> runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFEffectActionComponent : Component
{
}
