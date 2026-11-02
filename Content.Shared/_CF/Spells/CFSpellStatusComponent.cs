namespace Content.Shared.CF.Spells;

/// <summary>
/// Short-lived bonuses from spells such as shield, bless, and sure strike.
/// Server-only. Strikes read it before the d20.
/// </summary>
[RegisterComponent]
public sealed partial class CFSpellStatusComponent : Component
{
    [DataField]
    public int AcBonus;

    [DataField]
    public int AttackBonus;

    /// <summary>The next attack roll is made twice. Cleared when that attack is rolled.</summary>
    [DataField]
    public bool TrueStrike;

    /// <summary>Status bonus to saves from bless, bane, or heroism.</summary>
    [DataField]
    public int SaveBonus;

    /// <summary>Extra weapon damage dice from runic weapon.</summary>
    [DataField]
    public int ExtraWeaponDie;

    /// <summary>Non-zero while haste or slow has replaced the action cap.</summary>
    [DataField]
    public int ActionCap;

    [DataField]
    public TimeSpan Expires;
}
