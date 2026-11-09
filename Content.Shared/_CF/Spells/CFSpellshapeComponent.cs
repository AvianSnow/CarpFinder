using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>
/// Makes an action a spellshape: it changes the next spell the creature casts. Parent an action on
/// <c>CFActSpellshapeBase</c> and grant it with <c>CFGrantAction</c>. <see cref="CFSpellshapeSystem"/> runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFSpellshapeComponent : Component
{
    /// <summary>Added to the range of a spell that has one (Reach Spell).</summary>
    [DataField]
    public int ExtraRangeFeet;

    /// <summary>The spell is barely noticeable, so casting it does not set off reactions (Conceal Spell).</summary>
    [DataField]
    public bool Subtle;

    /// <summary>Amps the next psi cantrip, which spends a Focus Point. See <see cref="CFPsiSystem"/>.</summary>
    [DataField]
    public bool Amp;

    /// <summary>Makes a burst, cone or line with no duration bigger (Widen Spell).</summary>
    [DataField]
    public bool Widens;
}

/// <summary>On a creature that has used a spellshape and not yet cast the spell.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFActiveSpellshapeComponent : Component
{
    /// <summary>The action prototype of the spellshape waiting for a spell.</summary>
    [DataField, AutoNetworkedField]
    public EntProtoId Shape;

    [DataField, AutoNetworkedField]
    public TimeSpan Expires;

    /// <summary>The spellshape's own action is paid for on this tick, and that must not end it.</summary>
    public GameTick SetTick;
}
