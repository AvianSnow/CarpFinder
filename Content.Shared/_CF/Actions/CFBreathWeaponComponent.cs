using Content.Shared.Actions;

namespace Content.Shared.CF.Actions;

/// <summary>
/// A breath weapon or any other area a creature lets loose and then has to wait to use again, such as a dragon's Poison
/// Breath. Goes on an action parented on <c>CFActBreathBase</c>, with <c>CFAreaEffect</c>, <c>CFCheck</c> and
/// <c>CFDegreeEffects</c> saying what it does. The recharge is rolled each use and becomes the action's cooldown, so the
/// HUD shows it. <see cref="CFBreathWeaponSystem"/> runs it, for a player and for the AI alike.
/// </summary>
[RegisterComponent]
public sealed partial class CFBreathWeaponComponent : Component
{
    /// <summary>The recharge is this many dice of <see cref="RechargeDie"/>, in rounds.</summary>
    [DataField]
    public int RechargeDice = 1;

    [DataField]
    public int RechargeDie = 4;

    /// <summary>How long a round is. PF2e's 6 seconds, as everything timed in rounds here.</summary>
    [DataField]
    public float RoundSeconds = 6f;
}

public sealed partial class CFBreathWeaponEvent : WorldTargetActionEvent
{
}

/// <summary>Draconic Momentum: a critical success on a Strike recharges the creature's breath weapons.</summary>
[RegisterComponent]
public sealed partial class CFDraconicMomentumComponent : Component
{
}
