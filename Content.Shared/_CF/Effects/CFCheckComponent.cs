using Content.Shared.CF.Checks;

namespace Content.Shared.CF.Effects;

/// <summary>
/// The check each target faces: an attack roll against AC, or a save against a DC.
/// Without this component the source affects its targets with no roll.
/// </summary>
[RegisterComponent]
public sealed partial class CFCheckComponent : Component
{
    /// <summary><see cref="CFDefenseType.ArmorClass"/> is an attack roll by the user. Saves are rolled by the target.</summary>
    [DataField]
    public CFDefenseType Defense = CFDefenseType.None;

    /// <summary>
    /// Remaster basic save for damage: critical success none, success half, failure full, critical failure double.
    /// </summary>
    [DataField]
    public bool BasicSave = true;

    [DataField]
    public CFDcSource DcSource = CFDcSource.Spell;

    /// <summary>Used when <see cref="DcSource"/> is <see cref="CFDcSource.Fixed"/>.</summary>
    [DataField]
    public int FixedDc = 15;

    /// <summary>Added to the user's attack roll.</summary>
    [DataField]
    public int AttackModifier;

    /// <summary>Subtracted from the target's AC on an attack roll (phase bolt cuts circumstance bonuses).</summary>
    [DataField]
    public int TargetAcPenalty;

    /// <summary>An attack roll counts toward, and takes, the multiple attack penalty.</summary>
    [DataField]
    public bool MultipleAttackPenalty = true;
}

public enum CFDcSource : byte
{
    /// <summary>10 + spellcasting modifier + proficiency. Spell attack uses the same numbers.</summary>
    Spell,

    /// <summary>10 + key ability modifier + proficiency.</summary>
    Class,

    /// <summary><see cref="CFCheckComponent.FixedDc"/>, and an attack bonus of DC − 10. Hazards and items.</summary>
    Fixed,
}
