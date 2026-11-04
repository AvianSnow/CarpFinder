namespace Content.Shared.CF.Effects;

/// <summary>
/// What happens to each target, by the degree of the <see cref="CFCheckComponent"/> roll.
/// Degrees are written as the rules text gives them: the attacker's for attacks, the target's for saves.
/// </summary>
/// <remarks>
/// Order per target: <see cref="Effects"/>, then <see cref="Landed"/> if the check landed, then the list for the degree.
/// </remarks>
[RegisterComponent]
public sealed partial class CFDegreeEffectsComponent : Component
{
    /// <summary>
    /// Runs on every target whatever the degree. Damage here scales itself: basic save, or double on a critical hit.
    /// </summary>
    [DataField]
    public List<CFEffect> Effects = new();

    /// <summary>
    /// Runs when the check went the user's way: an attack hit, a save failed, or there was no check.
    /// </summary>
    [DataField]
    public List<CFEffect> Landed = new();

    [DataField]
    public List<CFEffect> CriticalSuccess = new();

    [DataField]
    public List<CFEffect> Success = new();

    [DataField]
    public List<CFEffect> Failure = new();

    [DataField]
    public List<CFEffect> CriticalFailure = new();
}
