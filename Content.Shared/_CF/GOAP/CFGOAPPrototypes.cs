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

    /// <summary>It holds a shield that is not up, and could raise it.</summary>
    ShieldReady = 1ul << 38,

    /// <summary>Its breath weapon is charged, it has the actions for it, and the target is inside the area.</summary>
    CanBreathe = 1ul << 40,

    /// <summary>It keeps a lair, and its target is too far away to go after.</summary>
    Guarding = 1ul << 41,

    /// <summary>It keeps a lair, and has gone too far from it or been away too long.</summary>
    LeftLair = 1ul << 42,

    /// <summary>It is a companion with a master it can walk to.</summary>
    HasMaster = 1ul << 43,

    // A tribe's life. CFGOAPSocietySensorSystem sets these, and only for a creature with CFGOAPSocial.

    /// <summary>Its hunger is low enough to do something about. It stays set until it has eaten its fill.</summary>
    Hungry = 1ul << 45,

    /// <summary>It is thirsty, and its tribe knows of water.</summary>
    Thirsty = 1ul << 46,

    /// <summary>It holds something to eat.</summary>
    HasFood = 1ul << 47,

    /// <summary>
    /// The food it holds is not ready to eat, and its tribe has what the next step needs: a fire for raw meat or
    /// dough, a mortar for wheat, water for flour, a knife for a loaf. See <c>CFGOAPTribeSystem.Stage</c>.
    /// </summary>
    FoodRaw = 1ul << 48,

    /// <summary>Food lies in sight, and it has a hand free for it.</summary>
    LooseFood = 1ul << 49,

    /// <summary>Its tribe's stores hold food, and it has a hand free for it.</summary>
    StoredFood = 1ul << 50,

    /// <summary>A plot of the tribe's soil has not been looked at for a while.</summary>
    SoilUnchecked = 1ul << 51,

    /// <summary>A plot was dry when last looked at, and there is a bucket and water to do something about it.</summary>
    SoilDry = 1ul << 52,

    /// <summary>A plot was low on nutrients when last looked at, and there is fertilizer to hand.</summary>
    SoilPoor = 1ul << 53,

    /// <summary>A plot was empty when last looked at, and there is a seed to hand.</summary>
    CanPlant = 1ul << 54,

    /// <summary>A plot had a crop to pick, or a dead plant to clear, when last looked at.</summary>
    CropReady = 1ul << 55,

    /// <summary>It holds food it does not need, and a hungry tribemate with none is near.</summary>
    MateHungry = 1ul << 56,

    /// <summary>A tribemate is near, nothing is wrong, and it has not talked for a while.</summary>
    WantsChat = 1ul << 57,

    /// <summary>Its tribe is talking to its target instead of fighting it. KillTarget does not count while this holds.</summary>
    Parleying = 1ul << 58,

    /// <summary>
    /// Something in or about the nest wants tidying, and it can do it: rubbish or a puddle near the nest, a coin or a
    /// potion lying loose with a store to put it in, or something in its own hand that nobody can use.
    /// </summary>
    NestUntended = 1ul << 63,

    // Never sensed. These only exist as what a goal wants, so a plan has something to reach.
    CropsTended = 1ul << 59,
    FoodStored = 1ul << 60,
    Socialized = 1ul << 61,
    Shared = 1ul << 62,
    TargetHurt = 1 << 20,
    Safe = 1 << 21,
    Idled = 1 << 22,
    Regrouped = 1 << 23,
    Waited = 1 << 24,
    Investigated = 1 << 25,
    TrapLaid = 1ul << 35,
    SidearmDrawn = 1ul << 36,
    ShieldUp = 1ul << 39,

    /// <summary>It carried out a Command order. Only the Obey goal wants it.</summary>
    Obeyed = 1ul << 37,

    /// <summary>It kept to its master. Only the StayWithMaster goal wants it, so a companion always has that to do.</summary>
    WithMaster = 1ul << 44,

    // Multiple Attack Penalty. It is what most changes the worth of a Strike, so a change in it has to replan.
    Attacked = 1 << 26,
    AttackedTwice = 1 << 27,
}

/// <summary>What running an action does.</summary>
public enum CFGOAPStep : byte
{
    /// <summary>Stay in reach, following the target if it moves, and let stock melee swing, which CF turns into Strikes.</summary>
    Strike,

    /// <summary>Stay put and let stock NPC shooting fire, which CF turns into Strikes.</summary>
    Shoot,

    /// <summary>Path to the target. When a Strike comes next, it already swings on the way in.</summary>
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

    /// <summary>Raise the shield it holds, for 1 action, the way a player does with use in hand.</summary>
    RaiseShield,

    /// <summary>Let its breath weapon loose at the target, the same action a player aims.</summary>
    Breathe,

    /// <summary>A companion keeps behind its master, and stops there when they stop.</summary>
    Follow,

    // A tribe's errands. Each walks to something and does one thing there. See Content.Server/_CF/GOAP/Society.

    /// <summary>Eat what it holds until it is full or the food is gone.</summary>
    Eat,

    /// <summary>Walk to the tribe's water and drink until it is no longer thirsty.</summary>
    Drink,

    /// <summary>Pick up loose food, or take some out of a store when the action says <c>fromStore</c>.</summary>
    TakeFood,

    /// <summary>Carry held food to one of the tribe's stores and put it in.</summary>
    StoreFood,

    /// <summary>
    /// Takes the food it holds one stage nearer to eating: grinds wheat at the mortar, mixes flour into water, holds
    /// raw meat or dough over the fire, or slices a loaf with the knife.
    /// </summary>
    Prepare,

    /// <summary>Walk to a plot and look at it: water, nutrients, and what grows there.</summary>
    InspectSoil,

    /// <summary>Fetch a bucket, fill it at the tribe's water, and pour it on a dry plot.</summary>
    WaterSoil,

    /// <summary>Work fertilizer into a poor plot.</summary>
    FeedSoil,

    /// <summary>Fetch a seed and plant it in an empty plot.</summary>
    PlantSeed,

    /// <summary>Pick a ripe crop, or clear a dead plant.</summary>
    Harvest,

    /// <summary>Hand held food to a hungry tribemate.</summary>
    GiveFood,

    /// <summary>Walk up to a tribemate and tell them the latest thing it remembers.</summary>
    Chat,

    /// <summary>Stand off from its target and make the tribe's demand, without attacking.</summary>
    Parley,

    /// <summary>Throws rubbish out of the nest, wipes up a puddle, or puts a loose valuable in a store.</summary>
    KeepNest,
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

    /// <summary><see cref="CFGOAPStep.TakeFood"/> takes from the tribe's stores, not from the ground.</summary>
    [DataField]
    public bool FromStore;

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

    /// <summary>
    /// How far out it starts slowing down when it gets somewhere or closes on a creature, in tiles. Smaller charges in
    /// harder and stops more abruptly.
    /// </summary>
    [DataField]
    public float ArriveRadius = 1.5f;

    /// <summary>The shortest rest between two idle walks, in seconds.</summary>
    [DataField]
    public float WanderRestMin = 1.5f;

    /// <summary>The longest rest between two idle walks, in seconds. A nervous creature rests less.</summary>
    [DataField]
    public float WanderRestMax = 4f;

    /// <summary>The furthest one idle walk goes, in tiles.</summary>
    [DataField]
    public float WanderDistance = 4f;

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
