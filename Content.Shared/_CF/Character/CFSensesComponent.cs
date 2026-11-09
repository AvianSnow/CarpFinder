using Robust.Shared.GameStates;

namespace Content.Shared.CF.Character;

/// <summary>Senses beyond normal sight, from <see cref="Grants.CFGrantSense"/>.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFSensesComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool LowLightVision;

    [DataField, AutoNetworkedField]
    public bool Darkvision;

    /// <summary>The reach of the longest imprecise sense. An undetected creature inside it is only hidden.</summary>
    [DataField, AutoNetworkedField]
    public int ImpreciseFeet;
}

public enum CFSense : byte
{
    LowLightVision,
    Darkvision,
    Scent,
    Tremorsense,
}
