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

    /// <summary>With <see cref="Amp"/>, which amp: the cantrip's own, or a feat's in its place (Mental Balm, Homing Beacon).</summary>
    [DataField]
    public CFAmpVariant AmpVariant = CFAmpVariant.Normal;

    /// <summary>Makes a burst, cone or line with no duration bigger (Widen Spell).</summary>
    [DataField]
    public bool Widens;

    /// <summary>
    /// The caster gains resistance equal to the spell's rank, for 6 seconds, to the energy the spell deals (Energy Ablation).
    /// The rules let the caster choose which energy.
    /// </summary>
    [DataField]
    public bool EnergyAblation;

    /// <summary>
    /// If the next spell has the air, water or cold trait and not fire, the ground under and around the first creature it
    /// affects fills with snow, difficult terrain for 6 seconds (Snowdrift Spell).
    /// </summary>
    [DataField]
    public bool Snowdrift;

    /// <summary>
    /// If the next spell is Heal or Harm restoring Hit Points to one creature, its melee Strikes deal 1d6 more of that energy
    /// for 6 seconds, 2d6 from rank 5 (Divine Infusion).
    /// </summary>
    [DataField]
    public bool DivineInfusion;

    /// <summary>The next Untamed Form is 2 ranks lower and lasts an hour (Form Control). <c>CFBattleFormSystem</c> reads it.</summary>
    [DataField]
    public bool FormControl;

    /// <summary>
    /// The caster gains temporary Hit Points for a minute equal to half the most damage the next spell deals one creature
    /// (Vampiric Spell).
    /// </summary>
    [DataField]
    public bool Vampiric;
}

/// <summary>The creature's last spell was cast with Vampiric Spell. <see cref="Most"/> is the most it has dealt one creature.</summary>
[RegisterComponent]
public sealed partial class CFVampiricPendingComponent : Component
{
    [DataField]
    public TimeSpan Until;

    [DataField]
    public int Most;
}

/// <summary>The creature's last spell was cast with Snowdrift Spell and has not yet affected a creature.</summary>
[RegisterComponent]
public sealed partial class CFSnowdriftPendingComponent : Component
{
    [DataField]
    public TimeSpan Until;
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
