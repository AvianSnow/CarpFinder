using System.Numerics;
using Content.Shared.CF.NPC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.NPC;

/// <summary>
/// The brain of an advanced NPC: a mini boss or a special encounter. Simple mobs stay on stock HTN and do not have this.
/// The root task has to be <c>CFGoapCompound</c>, which is what runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFGoapComponent : Component
{
    [DataField]
    public ProtoId<CFGoapProfilePrototype> Profile = "Default";

    [DataField]
    public List<ProtoId<CFGoapGoalPrototype>> Goals = new()
    {
        "KillTarget", "StayAlive", "Heal", "ArmSelf", "HuntTarget", "Idle",
    };

    [DataField]
    public List<ProtoId<CFGoapActionPrototype>> Actions = new()
    {
        "Strike", "Close", "Flee", "PickUpWeapon", "PickUpHealing", "DrinkHealing",
        "FollowScent", "GoToLkp", "CheckNode", "Wander",
    };

    public uint Facts;

    /// <summary>The creature it is after. It keeps this after losing sight of it, to know whose trail to follow.</summary>
    public EntityUid? Target;

    /// <summary>Last known position. This is the only place it may know the target is, once out of sight.</summary>
    public MapCoordinates? Lkp;

    /// <summary>Which way the target was heading when last seen.</summary>
    public Vector2 LkpHeading;

    public TimeSpan NextThink;

    public ProtoId<CFGoapGoalPrototype>? Goal;

    public List<ProtoId<CFGoapActionPrototype>> Plan = new();

    /// <summary>Where the plan has got to.</summary>
    public int Step;

    public bool StepStarted;

    public TimeSpan StepTime;

    /// <summary>A moving target's flanking slot is worked out again after this.</summary>
    public TimeSpan NextFlankUpdate;

    /// <summary>When it next checks whether the way ahead is clear.</summary>
    public TimeSpan NextNavigate;

    /// <summary>Whether it is following a flow field rather than heading straight there.</summary>
    public bool UsingField;

    /// <summary>The field of a creature with no group. In a group, the spawner holds one for all of them.</summary>
    public CFFlowField OwnField = new();

    /// <summary>The item the current step is going for.</summary>
    public EntityUid? Item;

    /// <summary>The node the current step is going to. Pack mates pick a different one.</summary>
    public EntityUid? Node;

    /// <summary>When each node was last checked by this creature.</summary>
    public Dictionary<EntityUid, TimeSpan> Checked = new();

    /// <summary>Nodes checked since it lost the target.</summary>
    public int NodesChecked;

    /// <summary>A goal a reflex is forcing, and until when.</summary>
    public ProtoId<CFGoapGoalPrototype>? ForcedGoal;

    public TimeSpan ForcedUntil;

    /// <summary>Allies already seen down, so a reflex fires once for each.</summary>
    public HashSet<EntityUid> FallenAllies = new();
}
