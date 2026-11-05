using Content.Shared.CF.Actions;
using Content.Shared.CF.Items;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.NPC;

/// <summary>What an advanced NPC can know about its situation. Sensors fill these in every think.</summary>
[Flags]
public enum CFGoapFact : uint
{
    None = 0,
    HasWeapon = 1 << 0,
    WeaponNearby = 1 << 1,
    HasHealingItem = 1 << 2,
    HealingNearby = 1 << 3,
    TargetVisible = 1 << 4,
    TargetInReach = 1 << 5,
    KnowsLkp = 1 << 6,
    AtLkp = 1 << 7,
    ScentNearby = 1 << 8,
    NodeNearby = 1 << 9,
    AlliesNearby = 1 << 10,
    ActionsLeft = 1 << 11,
    Hurt = 1 << 12,
    Prone = 1 << 13,
    Frightened = 1 << 14,

    /// <summary>On this creature's side of the target, opposite a pack mate.</summary>
    AtFlank = 1 << 15,

    /// <summary>Its group has broken.</summary>
    Routed = 1 << 16,

    Hiding = 1 << 17,

    /// <summary>It gave up on a door or window and has no other way to where it was going.</summary>
    NoWay = 1 << 18,

    // Never sensed. These only exist as what a goal wants, so a plan has something to reach.
    TargetHurt = 1 << 20,
    Safe = 1 << 21,
    Idled = 1 << 22,
    Regrouped = 1 << 23,
    Waited = 1 << 24,
}

/// <summary>What running an action does.</summary>
public enum CFGoapStep : byte
{
    /// <summary>Stay near and let stock melee swing, which CF turns into Strikes.</summary>
    Strike,

    /// <summary>Path to the target.</summary>
    Close,

    Flee,
    FollowScent,
    GoToLkp,
    CheckNode,
    PickUp,
    UseItem,
    Wander,
    Regroup,

    /// <summary>A player action, through <see cref="CFManeuverSystem.TryUse"/>.</summary>
    Maneuver,

    /// <summary>Go to this creature's side of the target.</summary>
    Flank,

    /// <summary>Stay put until thinking changes the plan.</summary>
    Wait,
}

/// <summary>Something an advanced NPC wants. The highest priority that can be planned for wins.</summary>
[Prototype("cfGoapGoal")]
public sealed partial class CFGoapGoalPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The facts that mean it is done.</summary>
    [DataField]
    public List<CFGoapFact> Wants = new();

    [DataField]
    public List<CFGoapFact> Avoids = new();

    [DataField]
    public float Priority = 1f;

    /// <summary>Added at no hit points, and scaled down to nothing at full.</summary>
    [DataField]
    public float WhenHurt;

    /// <summary>This goal only counts while these facts hold.</summary>
    [DataField]
    public List<CFGoapFact> OnlyWhen = new();

    [DataField]
    public List<CFGoapFact> NotWhen = new();

    /// <summary>Only counts below the profile's <see cref="CFGoapProfilePrototype.FleeHealth"/>.</summary>
    [DataField]
    public bool BelowFleeHealth;
}

/// <summary>One step a plan can use.</summary>
[Prototype("cfGoapAction")]
public sealed partial class CFGoapActionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public CFGoapStep Step;

    [DataField]
    public List<CFGoapFact> Needs = new();

    [DataField]
    public List<CFGoapFact> NeedsNot = new();

    [DataField]
    public List<CFGoapFact> Gives = new();

    [DataField]
    public List<CFGoapFact> Removes = new();

    [DataField]
    public float Cost = 1f;

    /// <summary>What <see cref="CFGoapStep.PickUp"/> looks for, or <see cref="CFGoapStep.UseItem"/> uses.</summary>
    [DataField]
    public CFItemUse Item;

    /// <summary>What <see cref="CFGoapStep.Maneuver"/> does.</summary>
    [DataField]
    public CFBasicAction Maneuver;
}

public enum CFGoapTrigger : byte
{
    /// <summary>There is a mark on the noise layer here.</summary>
    NoiseNearby,

    /// <summary>A creature on its side has gone down nearby.</summary>
    AllyDown,

    /// <summary>Hit points are under <see cref="CFSteeringReflex.Value"/>.</summary>
    HealthBelow,

    TargetSeen,
}

/// <summary>A personality's snap reaction: a trigger forces a goal for a while.</summary>
[DataDefinition]
public sealed partial class CFSteeringReflex
{
    [DataField(required: true)]
    public CFGoapTrigger Trigger;

    /// <summary>The share of hit points for <see cref="CFGoapTrigger.HealthBelow"/>.</summary>
    [DataField]
    public float Value;

    [DataField(required: true)]
    public ProtoId<CFGoapGoalPrototype> Goal;

    [DataField]
    public float Seconds = 3f;
}

/// <summary>
/// A personality. It only changes numbers and adds reflexes, so no personality needs its own code.
/// </summary>
[Prototype("cfGoapProfile")]
public sealed partial class CFGoapProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Scales speed.</summary>
    [DataField]
    public float MaxSpeed = 1f;

    /// <summary>How close it slows down when arriving.</summary>
    [DataField]
    public float ArriveRadius = 1f;

    /// <summary>How erratically it wanders.</summary>
    [DataField]
    public float WanderJitter = 0.5f;

    /// <summary>How strongly it keeps to walls. Stealthy creatures use this. It is not separation.</summary>
    [DataField]
    public float WallFollow;

    /// <summary>How strongly it stays away from allies. Keeps a pack spread out.</summary>
    [DataField]
    public float Separation;

    /// <summary>Seconds it keeps at a door or window it has to break before giving up.</summary>
    [DataField]
    public float SmashPatience = 20f;

    /// <summary>Seconds it looks around at the last known position.</summary>
    [DataField]
    public float LkpDwell = 3f;

    /// <summary>How many tiles from the last known position it stops. Cowards watch it from a distance.</summary>
    [DataField]
    public float LkpKeepOut;

    /// <summary>The share of hit points under which it runs. 0 never does.</summary>
    [DataField]
    public float FleeHealth;

    /// <summary>The share of hit points under which it counts as hurt.</summary>
    [DataField]
    public float HurtHealth = 0.5f;

    /// <summary>How many nodes it checks before giving up.</summary>
    [DataField]
    public int NodesToCheck = 4;

    [DataField]
    public Dictionary<ProtoId<CFGoapGoalPrototype>, float> GoalPriority = new();

    [DataField]
    public Dictionary<ProtoId<CFGoapActionPrototype>, float> ActionCost = new();

    [DataField]
    public Dictionary<CFNodeTag, float> NodeTags = new();

    [DataField]
    public List<CFSteeringReflex> Reflexes = new();
}
