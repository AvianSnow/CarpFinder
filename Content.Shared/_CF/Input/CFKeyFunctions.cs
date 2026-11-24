using Robust.Shared.Input;

namespace Content.Shared.CF.Input;

/// <summary>
/// CarpFinder's own key functions. The client adds them to the human input context (<c>CFInputSystem</c>), their
/// default keys are at the end of <c>Resources/keybinds.yml</c>, and the rebind menu lists them under CarpFinder.
/// A function's name is also its key in <c>keybinds.yml</c> and, in kebab case, in the locale
/// (<c>CfTrip</c> is <c>ui-options-function-cf-trip</c>), which is why the names are not written <c>CF</c>.
/// </summary>
[KeyFunctions]
public static class CFKeyFunctions
{
    public static readonly BoundKeyFunction CFTrip = "CfTrip";
    public static readonly BoundKeyFunction CFHighJump = "CfHighJump";
    public static readonly BoundKeyFunction CFLongJump = "CfLongJump";
}
