using Robust.Shared.Prototypes;

namespace Content.Shared.CF.GOAP;

/// <summary>A group of creatures that fight as one. A <c>CFEncounterSpawner</c> places it.</summary>
[Prototype("encounter")]
public sealed partial class CFGOAPEncounterPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>What to spawn. The list repeats until <see cref="Count"/> is reached.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Mobs = new();

    [DataField]
    public int Count = 1;

    [DataField]
    public ProtoId<CFGOAPProfilePrototype> Profile = "Default";

    [DataField]
    public ProtoId<CFGOAPTacticsPrototype> Tactics = "Default";

    /// <summary>The group runs once this share of it is left. 0 fights to the last.</summary>
    [DataField]
    public float RoutAt;

    /// <summary>Added to each creature's goals, on top of the usual ones.</summary>
    [DataField]
    public List<ProtoId<CFGOAPGoalPrototype>> Goals = new();

    /// <summary>Added to each creature's actions, on top of the usual ones.</summary>
    [DataField]
    public List<ProtoId<CFGOAPActionPrototype>> Actions = new();
}
