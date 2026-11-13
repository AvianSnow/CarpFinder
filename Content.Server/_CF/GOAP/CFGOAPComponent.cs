using System.Numerics;
using Content.Shared.CF.GOAP;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.GOAP;

/// <summary>
/// The brain of an advanced NPC: a mini boss or a special encounter. Simple mobs stay on stock HTN and do not have this.
/// The root task has to be <c>CFGOAPCompound</c>, which is what runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFGOAPComponent : Component
{
    /// <summary>Its personality: when it runs, how patient it is, how it moves.</summary>
    [DataField]
    public ProtoId<CFGOAPProfilePrototype> Profile = "Default";

    /// <summary>How it fights: who it goes for and what it thinks an action is worth.</summary>
    [DataField]
    public ProtoId<CFGOAPTacticsPrototype> Tactics = "Default";

    /// <summary>
    /// It fights unlike any group it joins, as a leader such as the kobold trapper does, so an encounter leaves its
    /// personality, tactics and lists alone. Without this, an encounter's settings replace the monster's own.
    /// </summary>
    [DataField]
    public bool KeepOwnLists;

    [DataField]
    public List<ProtoId<CFGOAPGoalPrototype>> Goals = new()
    {
        "PutOutFire", "RaiseShield", "KillTarget", "StayAlive", "Heal", "ArmSelf", "HuntTarget", "Investigate", "StayWithMaster", "GoHome", "Idle",
    };

    [DataField]
    public List<ProtoId<CFGOAPActionPrototype>> Actions = new()
    {
        "Breathe", "Strike", "Shoot", "Close", "Demoralize", "Feint", "RaiseShield", "HoldBack", "Flee", "FightBack", "PickUpWeapon", "PickUpHealing", "DrinkHealing",
        "FollowScent", "GoToLkp", "CheckNode", "LookAround", "GoHome", "Wander", "Follow", "PutOutFire",
    };

    public ulong Facts;

    /// <summary>The creature it is after. It keeps this after losing sight of it, to know whose trail to follow.</summary>
    public EntityUid? Target;

    /// <summary>Last known position. This is the only place it may know the target is, once out of sight.</summary>
    public MapCoordinates? Lkp;

    /// <summary>
    /// Where it started, relative to the grid, so a home on an airship moves with the ship. Set the first time it
    /// thinks, and not saved: a loaded map starts fresh from where each creature stands.
    /// </summary>
    public EntityCoordinates? Home;

    /// <summary>How far it may roam from <see cref="Home"/> with nothing to do, in tiles.</summary>
    [DataField]
    public float HomeRange = 8f;

    /// <summary>It has set off home, and will not stop at the edge of its range.</summary>
    public bool GoingHome;

    /// <summary>Which way the target was heading when last seen.</summary>
    public Vector2 LkpHeading;

    /// <summary>
    /// It has been to <see cref="Lkp"/>. This stays set when it walks away, or a node further off could never be
    /// reached: leaving would send it back to the last known position.
    /// </summary>
    public bool LkpReached;

    /// <summary>It found no way out, so until this time it stops trying to run and fights.</summary>
    public TimeSpan CorneredUntil;

    /// <summary>Who hurt it last, and until when it holds that against them.</summary>
    public EntityUid? HurtBy;

    public TimeSpan HurtByUntil;

    public TimeSpan NextThink;

    public ProtoId<CFGOAPGoalPrototype>? Goal;

    public List<ProtoId<CFGOAPActionPrototype>> Plan = new();

    /// <summary>Where the plan has got to.</summary>
    public int Step;

    public bool StepStarted;

    public TimeSpan StepTime;

    /// <summary>A moving target's flanking slot is worked out again after this.</summary>
    public TimeSpan NextFlankUpdate;

    /// <summary>The item the current step is going for.</summary>
    public EntityUid? Item;

    /// <summary>The node the current step is going to. Pack mates pick a different one.</summary>
    public EntityUid? Node;

    /// <summary>When each node was last checked by this creature.</summary>
    public Dictionary<EntityUid, TimeSpan> Checked = new();

    /// <summary>Nodes checked since it lost the target.</summary>
    public int NodesChecked;

    /// <summary>A goal a reflex is forcing, and until when.</summary>
    public ProtoId<CFGOAPGoalPrototype>? ForcedGoal;

    public TimeSpan ForcedUntil;

    /// <summary>Where the current idle walk is going, or null while it rests.</summary>
    public Vector2? WanderPoint;

    /// <summary>When the rest between idle walks ends.</summary>
    public TimeSpan WanderRestUntil;

    /// <summary>When an idle walk that has not got there is given up, and it rests where it is.</summary>
    public TimeSpan WanderGiveUpAt;

    /// <summary>
    /// The way its master was last seen walking, so a companion keeps behind them after they stop. Zero until they
    /// have moved.
    /// </summary>
    public Vector2 FollowHeading;

    /// <summary>When a companion next checks whether it can walk straight to its place behind its master.</summary>
    public TimeSpan NextFollowCheck;

    /// <summary>It could walk straight to its place behind its master when it last checked.</summary>
    public bool FollowStraight;

    /// <summary>Allies already seen down, so a reflex fires once for each.</summary>
    public HashSet<EntityUid> FallenAllies = new();

    /// <summary>A living player this close wakes the brain, in feet. See <c>CFGOAPSleepSystem</c>.</summary>
    [DataField]
    public float WakeFeet = 60f;

    /// <summary>How long no player has to be within <see cref="WakeFeet"/> before it sleeps again.</summary>
    [DataField]
    public TimeSpan SleepDelay = TimeSpan.FromSeconds(10);

    /// <summary>When a player was last within <see cref="WakeFeet"/>, or something last woke it.</summary>
    public TimeSpan LastWoken;

    public TimeSpan NextSleepCheck;
}
