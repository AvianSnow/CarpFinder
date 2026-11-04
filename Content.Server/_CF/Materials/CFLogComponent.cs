using Content.Shared.Tools;
using Robust.Shared.Prototypes;

namespace Content.Server._CF.Materials;

[RegisterComponent]
public sealed partial class CFLogComponent : Component
{
    [DataField(required: true)]
    public EntProtoId SpawnedPrototype;

    [DataField]
    public int SpawnCount = 2;

    [DataField]
    public ProtoId<ToolQualityPrototype> ToolQuality = "Slicing";
}
