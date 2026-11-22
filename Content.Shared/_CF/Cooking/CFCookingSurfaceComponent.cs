using Robust.Shared.GameStates;

namespace Content.Shared.CF.Cooking;

/// <summary>
/// A fire with a fuel store that is also somewhere to set things. Food, a pot or a mug used on it goes on top, where
/// <c>CFFireHeater</c> warms it, and only what the store takes (fuel) goes in. Without this the store claims every
/// click and refuses the dough as "too big". After CP14, whose furnace is where "the pot and pan should be placed".
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFCookingSurfaceComponent : Component;
