using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Spells;

/// <summary>
/// Marks a spell action: which spell it is, and the rank whose slot it spends.
/// What the spell does lives on the generic effect components in <c>Content.Shared.CF.Effects</c>:
/// <c>CFTargeting</c>, <c>CFAreaEffect</c>, <c>CFCheck</c>, <c>CFDegreeEffects</c>,
/// <c>CFProjectileDelivery</c>, <c>CFModeChoice</c>, and <c>CFHeighten</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFSpellComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<CFSpellPrototype> Spell;

    /// <summary>Costs a Focus Point instead of a slot. It uses the cantrip rank rule, so <see cref="Rank"/> stays 0.</summary>
    [DataField, AutoNetworkedField]
    public bool Focus;

    /// <summary>
    /// A sustained spell: once cast it runs its effects again every 6 seconds for a minute, with no input and at no cost,
    /// and only a dispel ends it sooner. See <see cref="CFSustain"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Sustained;

    /// <summary>
    /// With <see cref="Sustained"/>: casting it again while it runs moves it to the new spot, for the action's cost and no
    /// slot, as Floating Flame is moved. It burns there from its next pulse on, and its minute does not start over.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool MovesWhenRecast;

    /// <summary>A Magus's conflux spell. Casting one recharges Spellstrike.</summary>
    [DataField]
    public bool Conflux;

    /// <summary>0 is a cantrip: it does not spend a slot and heightens to half the caster's level.</summary>
    [DataField, AutoNetworkedField]
    public int Rank;

    /// <summary>The rank asked for by the next cast, from the client's menu. 0 takes the highest. Checked when it is paid for.</summary>
    [ViewVariables]
    public int ChosenRank;

    /// <summary>The rank of the slot the last cast was paid from. It lives here because a bolt can land after the cast.</summary>
    [ViewVariables]
    public int CastRank;

    /// <summary>The rank this cast resolves at: its slot's, when that is above the spell's own.</summary>
    public int ResolvedRank => Math.Max(Rank, CastRank);
}

public sealed partial class CFSpellCastInstantEvent : InstantActionEvent
{
}

public sealed partial class CFSpellCastTargetEvent : EntityTargetActionEvent
{
}

public sealed partial class CFSpellCastAreaEvent : WorldTargetActionEvent
{
}

/// <summary>Prestidigitation's tricks. These are the <c>CFModeChoice</c> values in its YAML.</summary>
public enum CFPrestidigitationTrick : byte
{
    None = 0,
    Food = 1,
    Lift = 2,
    Make = 3,
    Tidy = 4,
}
