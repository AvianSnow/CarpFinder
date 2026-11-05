using Content.Shared.Procedural;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Adventure;

/// <summary>
/// A kind of generated adventure site: its layout, and what can live there.
/// Replaces CP14's demiplanes, with PF2e encounter budgets in place of their danger modifiers.
/// </summary>
[Prototype("adventureSite")]
public sealed partial class CFAdventureSitePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<DungeonConfigPrototype> Layout;

    /// <summary>Solid wall around the open floor.</summary>
    [DataField(required: true)]
    public EntProtoId Wall;

    /// <summary>One encounter each, in order from the entrance to the deepest point.</summary>
    [DataField(required: true)]
    public List<CFThreat> Encounters = new();

    /// <summary>Creatures to build encounters from. Only those within 4 levels of the party are used.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Creatures = new();

    [DataField]
    public List<EntProtoId> Hazards = new();

    [DataField]
    public int HazardCount;

    [DataField]
    public Color Light = Color.FromHex("#3A4250");
}

/// <summary>Touching this generates its site for the toucher's company, and turns it into a two-way portal.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFAdventureRiftComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CFAdventureSitePrototype> Site;

    /// <summary>True while the site is being generated, so a second touch does not start another.</summary>
    [DataField]
    public bool Opening;
}
