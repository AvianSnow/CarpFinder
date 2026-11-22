using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Cooking;

/// <summary>
/// A mortar with its pestle. Using something it grinds on it, such as a wheat bushel, turns that into what the
/// recipe says, into the same hand. What it grinds is found by tag, so stock wheat and CF wheat both work.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFMortarComponent : Component
{
    /// <summary>What goes in, by a tag it carries, and what comes out.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<TagPrototype>, EntProtoId> Grinds = new();

    [DataField]
    public TimeSpan GrindTime = TimeSpan.FromSeconds(2);

    [DataField]
    public SoundSpecifier? Sound;
}

[Serializable, NetSerializable]
public sealed partial class CFGrindDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// An ingredient that is mixed into a liquid: flour used on a bucket or a well with enough water in it becomes dough.
/// The liquid is drawn out of what it was used on, and the result takes the ingredient's place in the hand.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFMixIntoComponent : Component
{
    [DataField]
    public ProtoId<ReagentPrototype> Reagent = "Water";

    /// <summary>How much of the reagent one of these takes.</summary>
    [DataField]
    public FixedPoint2 Amount = FixedPoint2.New(10);

    [DataField(required: true)]
    public EntProtoId Result;
}
