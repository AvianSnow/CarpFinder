using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// A weapon built for a larger creature, as a Giant instinct Barbarian carries. Whoever holds it is clumsy 1
/// (<see cref="CFConditionSystem.Clumsy"/>), and a Giant's Rage adds more damage with it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFOversizedWeaponComponent : Component
{
}
