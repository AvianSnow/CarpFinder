using Content.Shared.DoAfter;
using Content.Shared.Storage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Harvest;

/// <summary>
/// A monster whose body can be harvested for parts once it is dead, by someone trained in Survival with a survival
/// knife. Any stock butchering on the creature is taken over, so nobody butchers it without the check.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFHarvestableComponent : Component
{
    /// <summary>
    /// The meat, bone and leather the body gives, on top of <see cref="FallbackPart"/>. Their worth is the monster's
    /// parts value, shared between them all.
    /// </summary>
    [DataField]
    public List<EntitySpawnEntry> Parts = new();

    /// <summary>The generic monster parts every harvest gives.</summary>
    [DataField]
    public EntProtoId FallbackPart = "CFMonsterParts";

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    [DataField, AutoNetworkedField]
    public bool Harvested;
}

/// <summary>A tool that harvests monster parts: the survival knife.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFHarvestToolComponent : Component;

/// <summary>
/// The worth of a harvested part, per item in its stack, in copper. It replaces the prototype's price, because a
/// part is worth what its monster and the harvest made it. Parts are treasure, so the cleanup keeps them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFPartsValueComponent : Component
{
    [DataField, AutoNetworkedField]
    public int CopperPerUnit;
}

[Serializable, NetSerializable]
public sealed partial class CFHarvestDoAfterEvent : SimpleDoAfterEvent;
