using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared.CF.Actions;

public enum CFActivityKind : byte
{
    Rage,
    Flurry,
    Spellstrike,
    Anthem,
    SuddenCharge,
    Leap,
    ConstructTrap,
    SpellstrikeRecharge,
    MagusAnalysis,

    /// <summary>Venomtail Kobold: the next piercing or slashing hit within 6 seconds deals persistent poison.</summary>
    TailToxin,

    /// <summary>Envenom Fangs: the next unarmed hit within 6 seconds deals 1d6 persistent poison.</summary>
    EnvenomFangs,

    /// <summary>Demoralize the target, at whatever range the action allows (Terrifying Croak).</summary>
    Demoralize,

    /// <summary>Feint the target, at whatever range the action allows (Grovel).</summary>
    Feint,
}

/// <summary>
/// One radial activity. The kind selects the handler. The radial slot pays the action cost.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFActivityComponent : Component
{
    [DataField, AutoNetworkedField]
    public CFActivityKind Kind;
}

public sealed partial class CFActivityInstantEvent : InstantActionEvent
{
}

public sealed partial class CFActivityTargetEvent : EntityTargetActionEvent
{
}
