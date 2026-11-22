using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Cooking;

/// <summary>
/// A barrel that brews. Something it can mash (<see cref="Mashes"/>, by tag), used on it while it holds enough
/// water, turns that water into a mash. The mash then ferments by itself into a drink (<see cref="Ferments"/>),
/// a little at a time. Our own: CP14 only had drinks ready made.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class CFFermenterComponent : Component
{
    [DataField]
    public string Solution = "barrel";

    /// <summary>What can go in, by a tag it carries, and the mash it makes of the water.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<TagPrototype>, ProtoId<ReagentPrototype>> Mashes = new();

    /// <summary>What each mash ferments into.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<ReagentPrototype>, ProtoId<ReagentPrototype>> Ferments = new();

    [DataField]
    public ProtoId<ReagentPrototype> Water = "Water";

    /// <summary>How much water one ingredient turns into mash.</summary>
    [DataField]
    public FixedPoint2 WaterPerItem = FixedPoint2.New(10);

    /// <summary>How much of each mash ferments every <see cref="Interval"/>.</summary>
    [DataField]
    public FixedPoint2 UnitsPerUpdate = FixedPoint2.New(1);

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(3);

    [DataField, AutoPausedField]
    public TimeSpan NextUpdate;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/Fluids/splat.ogg");
}
