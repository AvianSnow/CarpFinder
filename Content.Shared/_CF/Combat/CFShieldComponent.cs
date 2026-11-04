using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// A held shield. Raise a Shield adds its circumstance bonus to AC. Shield Block reduces damage by its
/// Hardness, and the shield takes whatever damage gets through. At or below the Broken Threshold the
/// shield is broken (no AC bonus, no block). At 0 HP it is destroyed.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFShieldComponent : Component
{
    [DataField, AutoNetworkedField]
    public int AcBonus = 2;

    [DataField, AutoNetworkedField]
    public int Hardness = 5;

    [DataField, AutoNetworkedField]
    public int MaxHp = 20;

    /// <summary>Starts at <see cref="MaxHp"/> when left at -1 in YAML.</summary>
    [DataField, AutoNetworkedField]
    public int Hp = -1;

    [DataField, AutoNetworkedField]
    public int BrokenThreshold = 10;

    public int CurrentHp => Hp < 0 ? MaxHp : Hp;

    public bool Broken => CurrentHp <= BrokenThreshold;
}
