using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Pathfinder combat stats (AC / HP) for player characters and mobs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFCombatantComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Level = 1;

    [DataField, AutoNetworkedField]
    public int ArmorClass = 10;

    [DataField, AutoNetworkedField]
    public int MaxHp = 10;

    [DataField, AutoNetworkedField]
    public int CurrentHp = 10;

    [DataField, AutoNetworkedField]
    public string? ClassId;
}
