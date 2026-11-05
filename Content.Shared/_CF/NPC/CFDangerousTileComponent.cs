using Robust.Shared.GameStates;

namespace Content.Shared.CF.NPC;

/// <summary>
/// A square an advanced NPC must not step on, such as lava or a chasm. It steers away from these and paths
/// around them. Simple mobs on stock AI ignore this.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFDangerousTileComponent : Component;
