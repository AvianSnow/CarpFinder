using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Spells;

/// <summary>
/// Data for one CarpFinder spell action. The radial wheel casts it.
/// Area sizes are in feet on the real map grid (one tile is 5 feet), not melee reach.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFSpellComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<CFSpellPrototype> Spell;

    /// <summary>0 is a cantrip and does not spend a slot.</summary>
    [DataField, AutoNetworkedField]
    public int Rank;

    [DataField, AutoNetworkedField]
    public CFSpellDelivery Delivery = CFSpellDelivery.Target;

    [DataField, AutoNetworkedField]
    public CFSpellAreaKind Area = CFSpellAreaKind.None;

    [DataField, AutoNetworkedField]
    public int AreaFeet;

    /// <summary>0 means the effect comes from the caster (cone, line, emanation, or self).</summary>
    [DataField, AutoNetworkedField]
    public int RangeFeet;

    [DataField, AutoNetworkedField]
    public CFSpellDefense Defense = CFSpellDefense.None;

    /// <summary>
    /// Remaster basic save: critical success none, success half, failure full, critical failure double.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool BasicSave = true;

    [DataField, AutoNetworkedField]
    public int DiceCount;

    [DataField, AutoNetworkedField]
    public int DiceSize;

    [DataField, AutoNetworkedField]
    public bool AddSpellModifier;

    [DataField, AutoNetworkedField]
    public int FlatDamage;

    [DataField, AutoNetworkedField]
    public string DamageType = "force";

    [DataField, AutoNetworkedField]
    public int HealDice;

    [DataField, AutoNetworkedField]
    public int HealDie;

    [DataField, AutoNetworkedField]
    public int HealFlat;

    [DataField, AutoNetworkedField]
    public int AcBonus;

    [DataField, AutoNetworkedField]
    public int AttackBonus;

    /// <summary>Subtracted from the target's AC on a spell attack (phase bolt's circumstance cut).</summary>
    [DataField, AutoNetworkedField]
    public int TargetAcPenalty;

    /// <summary>Helpful spells affect you and creatures you cannot harm.</summary>
    [DataField, AutoNetworkedField]
    public bool Friendly;

    [DataField, AutoNetworkedField]
    public bool IncludeSelf;

    [DataField, AutoNetworkedField]
    public CFSpellRider Rider = CFSpellRider.None;

    [DataField, AutoNetworkedField]
    public int PushFeet = 5;

    [DataField, AutoNetworkedField]
    public float DurationSeconds = 60f;

    [DataField, AutoNetworkedField]
    public EntProtoId BoltPrototype = "CFSpellBolt";
}

public enum CFSpellDelivery : byte
{
    Instant,
    Target,
    Projectile,
    Area,
}

public enum CFSpellAreaKind : byte
{
    None,
    Burst,
    Cone,
    Emanation,
    Line,
    Square,
}

public enum CFSpellDefense : byte
{
    None,
    SpellAttack,
    Reflex,
    Fortitude,
    Will,
}

public enum CFSpellRider : byte
{
    None,
    Stabilize,
    Light,
    TrueStrike,
    Sleep,
    Prone,
    Push,
    Paralyze,
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
