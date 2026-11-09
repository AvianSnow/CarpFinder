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

    /// <summary>A Magus's conflux spell. Casting one recharges Spellstrike.</summary>
    [DataField]
    public bool Conflux;

    /// <summary>0 is a cantrip: it does not spend a slot and heightens to half the caster's level.</summary>
    [DataField, AutoNetworkedField]
    public int Rank;
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
