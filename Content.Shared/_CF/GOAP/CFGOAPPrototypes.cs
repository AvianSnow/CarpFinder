using Content.Shared.CF.Actions;
using Content.Shared.CF.Items;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.GOAP;

/// <summary>What an advanced NPC can know about its situation. Sensors fill these in every think.</summary>
[Flags]
public enum CFGOAPFact : ulong
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

    /// <summary>It tried to run, and every way out leads past what it is running from.</summary>
    Cornered = 1 << 19,

    /// <summary>Its side of the target is somewhere it cannot stand, such as on a table.</summary>
    FlankBlocked = 1 << 28,

    /// <summary>It is near where it started.</summary>
    AtHome = 1 << 29,

    /// <summary>It is burning, and standing somewhere it could put that out.</summary>
    OnFire = 1 << 30,

    /// <summary>It holds something loaded, and nothing stops a shot at its target.</summary>
    HasShot = 1u << 31,

    /// <summary>It fights at range, and an enemy has come closer than it likes.</summary>
    TooClose = 1ul << 32,

    /// <summary>It has a trap to lay, somewhere to lay it, and the enemy is not on top of it.</summary>
    CanLayTrap = 1ul << 33,

    /// <summary>Its ranged weapon is no use any more, and it has a melee weapon put away.</summary>
    ShouldDraw = 1ul << 34,

    // Never sensed. These only exist as what a goal wants, so a plan has something to reach.
    TargetHurt = 1 << 20,
    Safe = 1 << 21,
    Idled = 1 << 22,
    Regrouped = 1 << 23,
    Waited = 1 << 24,
    Investigated = 1 << 25,
    TrapLaid = 1ul << 35,
    SidearmDrawn = 1ul << 36,

    /// <summary>It carried out a Command order. Only the Obey goal wants it.</summary>
    Obeyed = 1ul << 37,

    // Multiple Attack Penalty. It is what most changes the worth of a Strike, so a change in it has to replan.
    Attacked = 1 << 26,
    AttackedTwice = 1 << 27,
}

/// <summary>What running an action does.</summary>
public enum CFGOAPStep : byte
{
    /// <summary>Stay near and let stock melee swing, which CF turns into Strikes.</summary>
    Strike,

    /// <summary>Stay put and let stock NPC shooting fire, which CF turns into Strikes.</summary>
    Shoot,

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

    /// <summary>Walk back to where it started.</summary>
    GoHome,

    /// <summary>Drop and roll until the fire is out.</summary>
    PutOutFire,

    /// <summary>Wait for the actions, then build a trap beside itself, on the side the target is.</summary>
    LayTrap,

    /// <summary>Drop what it is shooting with and take its melee weapon out of its pocket.</summary>
    DrawSidearm,

    /// <summary>Carry out a Command order: approach the caster, run from them, or halt.</summary>
    Obey,
}

/// <summary>How an action's worth is worked out when planning. See <c>CFGOAPTacticsSystem</c>.</summary>
public enum CFGOAPValue : byte
{
    /// <summary>It costs what its prototype says.</summary>
    None,

    /// <summary>Its share of a clean Strike, after Multiple Attack Penalty. Ruled out below the tactics' discipline.</summary>
    Strike,

    /// <summary>What frightening the target adds to the next few Strikes.</summary>
    Demoralize,

    /// <summary>What leaving the target off-guard adds to the next Strike.</summary>
    Feint,

    /// <summary>Waiting for the pool to refill. Only for tactics with any discipline.</summary>
    HoldBack,
}

/// <summary>Something an advanced NPC wants. The highest priority that can be planned for wins.</summary>
[Prototype("cfGoapGoal")]
public sealed partial class CFGOAPGoalPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The facts that mean it is done.</summary>
    [DataField]
    public List<CFGOAPFact> Wants = new();

    [DataField]
    public List<CFGOAPFact> Avoids = new();

    [DataField]
    public float Priority = 1f;

    /// <summary>Added at no hit points, and scaled down to nothing at full.</summary>
    [DataField]
    public float WhenHurt;

    /// <summary>This goal only counts while these facts hold.</summary>
    [DataField]
    public List<CFGOAPFact> OnlyWhen = new();

    [DataField]
    public List<CFGOAPFact> NotWhen = new();

    /// <summary>Only counts below the profile's <see cref="CFGOAPProfilePrototype.FleeHealth"/>.</summary>
    [DataField]
    public bool BelowFleeHealth;

    /// <summary>
    /// Nothing is going on. It thinks once a second instead of twice, casts no long rays to spot enemies, since its
    /// sight sensor does that, and its feelers only look where something is near.
    /// </summary>
    [DataField]
    public bool Calm;
}

/// <summary>One step a plan can use.</summary>
[Prototype("cfGoapAction")]
public sealed partial class CFGOAPActionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public CFGOAPStep Step;

    [DataField]
    public List<CFGOAPFact> Needs = new();

    [DataField]
    public List<CFGOAPFact> NeedsNot = new();

    [DataField]
    public List<CFGOAPFact> Gives = new();

    [DataField]
    public List<CFGOAPFact> Removes = new();

    [DataField]
    public float Cost = 1f;

    /// <summary>What <see cref="CFGOAPStep.PickUp"/> looks for, or <see cref="CFGOAPStep.UseItem"/> uses.</summary>
    [DataField]
    public CFItemUse Item;

    /// <summary>What <see cref="CFGOAPStep.Maneuver"/> does.</summary>
    [DataField]
    public CFBasicAction Maneuver;

    /// <summary>How its worth changes with the fight. Its cost is divided by that worth, and nothing means ruled out.</summary>
    [DataField]
    public CFGOAPValue ValuedBy;

    /// <summary>
    /// <see cref="CFGOAPStep.GoToLkp"/> stops caring about the place once it has looked around. Leave this off when the
    /// plan goes on to something else there, such as checking the nodes around it.
    /// </summary>
    [DataField]
    public bool ForgetsLkp;
}

public enum CFGOAPTrigger : byte
{
    /// <summary>There is a mark on the noise layer here.</summary>
    NoiseNearby,

    /// <summary>A creature on its side has gone down nearby.</summary>
    AllyDown,

    /// <summary>Hit points are under <see cref="CFGOAPReflex.Value"/>.</summary>
    HealthBelow,

    TargetSeen,
}

/// <summary>A personality's snap reaction: a trigger forces a goal for a while.</summary>
[DataDefinition]
public sealed partial class CFGOAPReflex
{
    [DataField(required: true)]
    public CFGOAPTrigger Trigger;

    /// <summary>The share of hit points for <see cref="CFGOAPTrigger.HealthBelow"/>.</summary>
    [DataField]
    public float Value;

    [DataField(required: true)]
    public ProtoId<CFGOAPGoalPrototype> Goal;

    [DataField]
    public float Seconds = 3f;
}

/// <summary>
/// A personality. It only changes numbers and adds reflexes, so no personality needs its own code.
/// </summary>
[Prototype("cfGoapProfile")]
public sealed partial class CFGOAPProfilePrototype : IPrototype
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
    public Dictionary<ProtoId<CFGOAPGoalPrototype>, float> GoalPriority = new();

    [DataField]
    public Dictionary<ProtoId<CFGOAPActionPrototype>, float> ActionCost = new();

    [DataField]
    public Dictionary<CFGOAPNodeTag, float> NodeTags = new();

    [DataField]
    public List<CFGOAPReflex> Reflexes = new();
}
