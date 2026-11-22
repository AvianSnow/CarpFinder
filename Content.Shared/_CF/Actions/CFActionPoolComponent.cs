using Robust.Shared.GameStates;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Action stamina. One action spends <see cref="StaminaPerAction"/> points.
/// A point returns every <see cref="RegenSeconds"/>. <see cref="MaxActions"/> is per creature,
/// so haste or a potion can raise it past the usual three.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFActionPoolComponent : Component
{
    /// <summary>How many actions this creature can hold.</summary>
    [DataField, AutoNetworkedField]
    public int MaxActions = 3;

    /// <summary>Stamina spent to take one action.</summary>
    [DataField, AutoNetworkedField]
    public float StaminaPerAction = 1f;

    /// <summary>
    /// Stamina available now. Negative means "start full" and is filled on map init.
    /// Full is <see cref="MaxActions"/> times <see cref="StaminaPerAction"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Stamina = -1f;

    /// <summary>Seconds to regain one point of stamina while a player is in the body.</summary>
    [DataField, AutoNetworkedField]
    public float RegenSeconds = 1.2f;

    /// <summary>Seconds to regain one point of stamina with no player in the body.</summary>
    [DataField, AutoNetworkedField]
    public float NpcRegenSeconds = 1.5f;

    /// <summary>When an NPC has finished thinking about its next attack. Zero means it has not started.</summary>
    public TimeSpan ThinkingUntil;

    /// <summary>
    /// Strikes and spell attacks already made since this pool was last full.
    /// A cantrip or other action with no attack roll does not increase it.
    /// The next strike takes −5/−10, or −4/−8 when the weapon is agile.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MapAttacks;

    /// <summary>A Step already paid for covers any other enemy's reach left before this time.</summary>
    [DataField]
    public TimeSpan StepCoveredUntil;

    /// <summary>A flourish action was used since this pool was last full. Only one is allowed.</summary>
    [DataField, AutoNetworkedField]
    public bool Flourished;

    /// <summary>The creature this one last Struck at, for the sweep trait. Only meaningful while <see cref="MapAttacks"/> is above 0.</summary>
    public EntityUid? LastStrikeTarget;

    /// <summary>Time accumulated toward the next point. Not networked; the server owns regen.</summary>
    [DataField]
    public float RegenProgress;

    /// <summary>Local throttle so a held click does not stack empty-pool popups.</summary>
    public TimeSpan NextEmptyPopup;
}
