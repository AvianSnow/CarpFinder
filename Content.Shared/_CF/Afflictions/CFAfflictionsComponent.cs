using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Afflictions;

/// <summary>Afflictions running on a creature. Server-only state.</summary>
[RegisterComponent]
public sealed partial class CFAfflictionsComponent : Component
{
    [DataField]
    public List<CFActiveAffliction> Active = new();
}

[DataDefinition]
public sealed partial class CFActiveAffliction
{
    [DataField]
    public ProtoId<CFAfflictionPrototype> Id;

    /// <summary>Current stage, from 1. Zero during the onset.</summary>
    [DataField]
    public int Stage;

    /// <summary>The stage the onset leads to.</summary>
    [DataField]
    public int PendingStage;

    /// <summary>When the onset ends or the next save is due.</summary>
    [DataField]
    public TimeSpan NextTick;

    [DataField]
    public TimeSpan NextExposure;
}
