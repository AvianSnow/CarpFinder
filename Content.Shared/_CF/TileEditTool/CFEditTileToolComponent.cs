using Content.Shared.DoAfter;
using Content.Shared.Maps;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.TileEditTool;

[RegisterComponent, Access(typeof(CFEditTileToolSystem))]
public sealed partial class CFEditTileToolComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(1f);

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public Dictionary<ProtoId<ContentTileDefinition>, ProtoId<ContentTileDefinition>> TileReplace = new();
}

[Serializable, NetSerializable]
public sealed partial class CFTileToolReplaceDoAfter : DoAfterEvent
{
    [DataField(required:true)]
    public NetCoordinates Coordinates;

    public CFTileToolReplaceDoAfter(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }

    public override DoAfterEvent Clone() => this;
}
