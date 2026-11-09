using Content.Shared.CF.Effects;

namespace Content.Shared.CF.Actions;

/// <summary>
/// An action that is a Strike with something extra. Parent an action on <c>CFActStrikeBase</c>, say what it needs and what a hit
/// does, and grant it with <c>CFGrantAction</c>. <see cref="CFStrikeActionSystem"/> runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFStrikeActionComponent : Component
{
    /// <summary>Needs a loaded ranged weapon in hand, and spends the arrow. Otherwise it is a melee Strike.</summary>
    [DataField]
    public bool Ranged;

    /// <summary>It has to be an unarmed attack.</summary>
    [DataField]
    public bool UnarmedOnly;

    /// <summary>Needs a melee weapon in each hand, and makes one Strike with each.</summary>
    [DataField]
    public bool TwoWeapons;

    /// <summary>Needs a one-handed melee weapon and a free hand. The weapon hits harder for this Strike.</summary>
    [DataField]
    public bool TwoHandedGrip;

    /// <summary>Only against the creature the user is hunting.</summary>
    [DataField]
    public bool PreyOnly;

    /// <summary>Only against a prone creature.</summary>
    [DataField]
    public bool ProneTarget;

    /// <summary>A finisher: it needs panache, spends it, and adds the finisher's precision damage.</summary>
    [DataField]
    public bool Finisher;

    /// <summary>More weapon damage dice on this Strike.</summary>
    [DataField]
    public int ExtraDice;

    /// <summary>Added to the attack roll.</summary>
    [DataField]
    public int AttackBonus;

    /// <summary>True when the effects need the Strike to deal damage, and not only to hit.</summary>
    [DataField]
    public bool EffectsNeedDamage = true;

    /// <summary>Runs on the target after a hit.</summary>
    [DataField]
    public List<CFEffect> OnHit = new();

    /// <summary>Runs instead of <see cref="OnHit"/> on a critical hit, if it has anything in it.</summary>
    [DataField]
    public List<CFEffect> OnCritical = new();
}
