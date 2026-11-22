namespace Content.Shared.CF.Actions;

/// <summary>
/// Marks an action as a flourish. A creature can use one flourish until its action pool is next full.
/// <see cref="CFActionPoolSystem"/> enforces it, so an action only needs this component.
/// </summary>
[RegisterComponent]
public sealed partial class CFFlourishComponent : Component
{
}
