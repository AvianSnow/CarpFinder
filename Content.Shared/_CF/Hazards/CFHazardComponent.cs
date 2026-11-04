using Content.Shared.CF.Preferences;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.Hazards;

/// <summary>
/// A hidden hazard (trap). Creatures that come within <see cref="DetectRadiusFeet"/> roll Perception against
/// <see cref="StealthDc"/> once; on a success they can see it and try to disable it. Anyone who steps into
/// <see cref="TriggerSizeFeet"/> sets it off, which resolves the hazard's own effect components
/// (<c>CFCheck</c>, <c>CFDegreeEffects</c>) on that creature.
/// </summary>
/// <remarks>
/// A hazard with a <c>CFFaction</c> (laid by a kobold trapper, say) is known to that faction's allies:
/// they see it, never roll for it, and never set it off.
/// Object stats (AC, HP, Hardness) are a <c>CFCombatant</c> and <c>CFMaterial</c> on the same entity.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFHazardComponent : Component
{
    public const string DetectFixture = "cf-hazard-detect";
    public const string TriggerFixture = "cf-hazard-trigger";

    /// <summary>Hazard level. XP for disabling it comes from this level against the party's.</summary>
    [DataField, AutoNetworkedField]
    public int Level;

    /// <summary>Complex hazards (with a routine) are worth creature XP. Simple ones are worth less.</summary>
    [DataField]
    public bool Complex;

    /// <summary>Perception DC to notice it.</summary>
    [DataField, AutoNetworkedField]
    public int StealthDc = 10;

    /// <summary>Creatures this close roll Perception to notice the hazard.</summary>
    [DataField]
    public float DetectRadiusFeet = 30f;

    /// <summary>Width of the area that sets it off, centered on the hazard. 5 feet is its own square.</summary>
    [DataField]
    public float TriggerSizeFeet = 5f;

    /// <summary>The ways to disable it. More than one shows a "Disarm trap" submenu.</summary>
    [DataField]
    public List<CFHazardDisarm> Disarm = new();

    /// <summary>Actions a disable attempt costs (Disable a Device is 2).</summary>
    [DataField]
    public int DisarmActions = 2;

    /// <summary>False once it has gone off. A spent hazard stays visible and does nothing.</summary>
    [DataField, AutoNetworkedField]
    public bool Armed = true;

    /// <summary>Everyone can see it: it went off, or a mapper placed it in plain sight.</summary>
    [DataField, AutoNetworkedField]
    public bool Revealed;

    /// <summary>Creatures that noticed it. Only they see it and can disable it.</summary>
    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> DetectedBy = new();

    /// <summary>Creatures that already rolled to notice it. Each rolls once.</summary>
    [DataField]
    public HashSet<EntityUid> Rolled = new();
}

/// <summary>One way to disable a hazard: a skill check against a DC.</summary>
[DataDefinition]
public sealed partial class CFHazardDisarm
{
    /// <summary>Skill name as on the character sheet: Thievery, Athletics, Arcana, …</summary>
    [DataField(required: true)]
    public string Skill = "Thievery";

    [DataField]
    public CFAbility Ability = CFAbility.Dexterity;

    [DataField(required: true)]
    public int Dc = 15;
}

/// <summary>
/// Raised on a hazard when <see cref="User"/> disables it, just before it is deleted.
/// The server awards the user's company XP for the hazard's level.
/// </summary>
[ByRefEvent]
public readonly record struct CFHazardDisarmedEvent(EntityUid User, int Level, bool Complex);
