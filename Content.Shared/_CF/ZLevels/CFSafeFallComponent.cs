namespace Content.Shared.CF.ZLevels;

/// <summary>This creature takes no damage from falling.</summary>
[RegisterComponent]
public sealed partial class CFSafeFallComponent : Component
{
    /// <summary>It only works with a hand free, as with webbed hands.</summary>
    [DataField]
    public bool NeedsFreeHand;
}
