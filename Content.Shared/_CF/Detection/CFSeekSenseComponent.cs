using Content.Shared.CF.Traits;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Detection;

/// <summary>
/// Keener senses for finding creatures: a bonus to Seek within a range, and easier flat checks against creatures that are
/// concealed or hidden inside it. Whisper Elves hear them, Longsnout Rats smell them. Given by <c>CFGrantSeek</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFSeekSenseComponent : Component
{
    /// <summary>A circumstance bonus to Seek a creature within <see cref="RangeFeet"/>.</summary>
    [DataField, AutoNetworkedField]
    public int SeekBonus;

    [DataField, AutoNetworkedField]
    public int RangeFeet = 30;

    /// <summary>The flat check DC to target a hidden creature in range. 0 keeps the usual 11.</summary>
    [DataField, AutoNetworkedField]
    public int HiddenFlatDc;

    /// <summary>The flat check DC to target a concealed creature in range. 0 keeps the usual 5.</summary>
    [DataField, AutoNetworkedField]
    public int ConcealedFlatDc;

    /// <summary>When set, the sense only finds creatures with one of these traits (Apparition Sense: undead and spirits).</summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<CFTraitPrototype>> Traits = new();
}

public static class CFSeekSenses
{
    /// <summary>Whether the sense works on this creature at all: any creature, or one with a trait it is for.</summary>
    public static bool Covers(IEntityManager entities, CFSeekSenseComponent sense, EntityUid target)
    {
        return sense.Traits.Count == 0 || entities.GetComponentOrNull<CFTraitComponent>(target)?.HasAny(sense.Traits) == true;
    }
}
