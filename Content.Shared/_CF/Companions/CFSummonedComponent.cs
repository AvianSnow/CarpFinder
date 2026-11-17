using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Companions;

/// <summary>
/// A creature called by a spell or ability, such as a thrall. It carries the Summoned trait. When it dies, or its
/// time is up, it vanishes in a puff of smoke and leaves no body, so it cannot be looted or harvested.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFSummonedComponent : Component
{
    /// <summary>When a summoning spell's creature goes back. Zero means it stays until it dies or is sent away.</summary>
    [DataField(customTypeSerializer: typeof(Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.TimeOffsetSerializer))]
    public TimeSpan Until;

    /// <summary>Called by a summoning spell. A caster keeps one such creature: the next spell sends this one back.</summary>
    [DataField]
    public bool FromSpell;

    /// <summary>What it leaves behind as it goes.</summary>
    [DataField]
    public EntProtoId Puff = "CFSummonPuff";
}

/// <summary>One creature a summoning spell can call, and the lowest rank of the spell that calls it.</summary>
[DataDefinition]
public sealed partial class CFSummonOption
{
    [DataField(required: true)]
    public int Rank;

    /// <summary>A creature parented on its monster and on <c>CFSummonedCreature</c>. See <c>Entities/Mobs/summons.yml</c>.</summary>
    [DataField(required: true)]
    public EntProtoId Prototype;
}

/// <summary>
/// Asks the server to call a creature for <paramref name="Summoner"/>. The effect is in Shared and the companion code is
/// on the server, so it goes by event. <see cref="Summoned"/> is filled in when it has been done.
/// </summary>
[ByRefEvent]
public record struct CFSummonRequestEvent(EntityUid Summoner, EntProtoId Prototype, Robust.Shared.Map.EntityCoordinates Where, float Seconds)
{
    public EntityUid? Summoned;
}
