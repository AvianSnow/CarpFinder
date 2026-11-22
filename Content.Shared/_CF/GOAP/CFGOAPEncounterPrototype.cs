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

    /// <summary>Replaces each creature's own personality. Left out, each keeps its own.</summary>
    [DataField]
    public ProtoId<CFGOAPProfilePrototype>? Profile;

    /// <summary>Replaces each creature's own tactics, such as a boar's Beast. Left out, each keeps its own.</summary>
    [DataField]
    public ProtoId<CFGOAPTacticsPrototype>? Tactics;

    /// <summary>The group runs once this share of it is left. 0 fights to the last.</summary>
    [DataField]
    public float RoutAt;

    /// <summary>
    /// The group lives as a tribe: it keeps a nest, eats, drinks, farms, stores food and talks to strangers before
    /// fighting them. See <c>CFGOAPTribeSystem</c>.
    /// </summary>
    [DataField]
    public bool Tribe;

    /// <summary>Added to each creature's goals, on top of its own. A brain with <c>keepOwnLists</c> is left alone.</summary>
    [DataField]
    public List<ProtoId<CFGOAPGoalPrototype>> Goals = new();

    /// <summary>Added to each creature's actions, on top of its own. A brain with <c>keepOwnLists</c> is left alone.</summary>
    [DataField]
    public List<ProtoId<CFGOAPActionPrototype>> Actions = new();

    /// <summary>Things laid down beside the group when it spawns: a tribe's mortar, bucket, knife and clippers.</summary>
    [DataField]
    public List<EntProtoId> Supplies = new();
}
