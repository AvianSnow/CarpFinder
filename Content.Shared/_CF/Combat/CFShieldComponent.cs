using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// A held shield. Raise a Shield adds its AC bonus. Shield Block spends its hardness once per turn.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFShieldComponent : Component
{
    [DataField, AutoNetworkedField]
    public int AcBonus = 2;

    [DataField, AutoNetworkedField]
    public int Hardness = 5;
}
