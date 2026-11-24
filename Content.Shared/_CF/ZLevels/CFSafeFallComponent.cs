namespace Content.Shared.CF.ZLevels;

/// <summary>This creature takes no damage from falling.</summary>
[RegisterComponent]
public sealed partial class CFSafeFallComponent : Component
{
    /// <summary>It only works with a hand free, as with webbed hands.</summary>
    [DataField]
    public bool NeedsFreeHand;

    /// <summary>Falls still hurt, but count as half their distance (Unbreakable Goblin).</summary>
    [DataField]
    public bool HalfDistance;

    /// <summary>Falls still hurt, but count as this many feet shorter (Cat Fall: 10, which is one level).</summary>
    [DataField]
    public int FeetLess;
}
