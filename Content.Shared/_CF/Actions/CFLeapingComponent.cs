namespace Content.Shared.CF.Actions;

/// <summary>
/// On a creature in the air from a Leap, from <c>CFActivitySystem.LeapBy</c> until the throw ends. A Leap covers a
/// fixed distance, so the creature stops where it lands instead of sliding on, and whatever is under it there,
/// floor or an opening, is what it lands on.
/// </summary>
[RegisterComponent]
public sealed partial class CFLeapingComponent : Component;
