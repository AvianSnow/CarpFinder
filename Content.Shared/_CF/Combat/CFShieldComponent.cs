using Robust.Shared.GameStates;
using Robust.Shared.Map;

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

    /// <summary>Raised on the shield itself: it stays up in either hand until lowered, let go of, or broken.</summary>
    [DataField, AutoNetworkedField]
    public bool Raised;

    /// <summary>Feet of Speed lost while held. Strength does not reduce it. Negative.</summary>
    [DataField, AutoNetworkedField]
    public int SpeedPenalty;

    /// <summary>A raised shield with this offers Take Cover: 1 action, and this AC bonus instead of the usual one.</summary>
    [DataField, AutoNetworkedField]
    public int TakeCoverBonus;

    /// <summary>Where the holder took cover. Leaving this spot ends it.</summary>
    [DataField, AutoNetworkedField]
    public EntityCoordinates? CoverPosition;

    public int CurrentHp => Hp < 0 ? MaxHp : Hp;

    public bool Broken => CurrentHp <= BrokenThreshold;
}
