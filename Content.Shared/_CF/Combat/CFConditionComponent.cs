using Robust.Shared.GameStates;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Short combat conditions Strikes and movement read.
/// Timed fields are server-only. The flags replicate so movement and examine stay honest.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFConditionComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Frightened;

    [DataField, AutoNetworkedField]
    public bool Grabbed;

    [DataField, AutoNetworkedField]
    public bool Immobilized;

    [DataField, AutoNetworkedField]
    public bool Concealed;

    /// <summary>Flat check a creature must pass to affect this one. 5 is concealed. 11 is hidden.</summary>
    [DataField, AutoNetworkedField]
    public int FlatCheckDc;

    /// <summary>Casting or Striking ends this concealment. Invisibility does. Blur does not.</summary>
    [DataField, AutoNetworkedField]
    public bool ConcealBreaks;

    [DataField, AutoNetworkedField]
    public bool Sanctuary;

    [DataField, AutoNetworkedField]
    public int SanctuaryDc;

    [DataField, AutoNetworkedField]
    public bool Charmed;

    [DataField, AutoNetworkedField]
    public bool Calmed;

    [DataField, AutoNetworkedField]
    public bool Blinded;

    [DataField, AutoNetworkedField]
    public bool RaisedShield;

    /// <summary>Hardness of a shield spell that has not blocked yet. 0 means none.</summary>
    [DataField, AutoNetworkedField]
    public int ShieldHardness;

    [DataField, AutoNetworkedField]
    public bool OffGuardAll;

    public EntityUid? Grabber;

    public EntityUid? Charmer;

    public HashSet<EntityUid> OffGuardTo = new();

    public Dictionary<EntityUid, TimeSpan> DemoralizedUntil = new();

    public TimeSpan ImmobilizedUntil;

    public TimeSpan ConcealedUntil;

    public TimeSpan SanctuaryUntil;

    public TimeSpan CharmedUntil;

    public TimeSpan CalmedUntil;

    public TimeSpan BlindUntil;

    public TimeSpan OffGuardAllUntil;

    public Dictionary<EntityUid, TimeSpan> OffGuardUntil = new();
}

/// <summary>Conditions an effect can apply through <see cref="CFConditionSystem.ApplyCondition"/>.</summary>
public enum CFConditionKind : byte
{
    /// <summary>Value is the frightened value.</summary>
    Frightened,
    Prone,

    /// <summary>Knocked down and unconscious for the duration (sleep).</summary>
    Asleep,
    Paralyzed,
    Immobilized,

    /// <summary>DC 5 flat check to target (blur).</summary>
    Concealed,

    /// <summary>DC 11 flat check to target. Ends when the creature attacks or casts (invisibility).</summary>
    Invisible,

    /// <summary>Attackers must pass a Will save against the effect's DC.</summary>
    Sanctuary,
    Charmed,
    Calmed,
    Blinded,
    Darkvision,

    /// <summary>Blocks damage once. Value is the hardness (shield cantrip).</summary>
    SpellShield,
    OffGuard,
}
