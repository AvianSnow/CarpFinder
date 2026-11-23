namespace Content.Shared.CF.Actions;

/// <summary>
/// An action that only applies effects: <c>CFDegreeEffects</c>, and <c>CFCheck</c> if it rolls one. Parent an action on
/// <c>CFActivityInstant</c> for one on yourself or <c>CFActivityTarget</c> for one on a creature, and grant it with
/// <c>CFGrantAction</c>. <see cref="CFEffectActionSystem"/> runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFEffectActionComponent : Component
{
    /// <summary>Needs a melee weapon in each hand, as Twin Parry does.</summary>
    [DataField]
    public bool NeedsWeaponInEachHand;

    /// <summary>Needs medium or heavy armor worn, as Armored Courage does.</summary>
    [DataField]
    public bool NeedsMediumOrHeavyArmor;

    /// <summary>Needs both hands empty, as Rabid Sprint does.</summary>
    [DataField]
    public bool NeedsBothHandsFree;

    /// <summary>Needs a weapon held in both hands: a melee one, a ranged one, or either.</summary>
    [DataField]
    public CFTwoHandedNeed NeedsTwoHanded = CFTwoHandedNeed.No;

    /// <summary>Needs a one-handed melee weapon in one hand and nothing in the other, as Dueling Parry does.</summary>
    [DataField]
    public bool NeedsWeaponAndFreeHand;

    /// <summary>Only within 6 seconds of the user's blood magic taking effect (Bleed Out).</summary>
    [DataField]
    public bool NeedsBloodMagic;

    /// <summary>
    /// The cursebound trait: using it raises an Oracle's cursebound value by 1, and it cannot be used at the most.
    /// See <c>CFCurseboundSystem</c>.
    /// </summary>
    [DataField]
    public bool Cursebound;

    /// <summary>Only within 6 seconds of casting a damaging spell that was not a cantrip (Foretell Harm).</summary>
    [DataField]
    public bool NeedsDamagingSpell;

    /// <summary>Only while raging: the rage trait.</summary>
    [DataField]
    public bool NeedsRaging;

    /// <summary>The target has to be a dead enemy, as Absorb Strength needs a corpse.</summary>
    [DataField]
    public bool NeedsDeadEnemy;

    /// <summary>An Inventor's unstable action: it needs the innovation in use and working, and may stop it afterwards.</summary>
    [DataField]
    public bool Unstable;
}

public enum CFTwoHandedNeed : byte
{
    No,
    Melee,
    Ranged,
}
