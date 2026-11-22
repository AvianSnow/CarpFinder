// Ported from Starlight (https://github.com/ss14Starlight/space-station-14), their Scent system, with permission.
// Cut down for CarpFinder: no sneezing, no sniffing objects for traces, no cleaning.

using Robust.Shared.GameStates;

namespace Content.Shared.CF.Scent;

/// <summary>
/// A creature with a scent of its own. Every so often it leaves a <see cref="CFScentMarkerComponent"/> where it stands, so a
/// creature with the Scent sense (<see cref="CFSmellerComponent"/>) can see the trail it left. The AI follows scent through
/// the influence maps instead (<c>CFGOAPScentEmitter</c>); this is what players see.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFScentComponent : Component
{
    /// <summary>This creature's own scent, which colours its trail. Null until map init rolls one.</summary>
    [DataField, AutoNetworkedField]
    public string? ScentId;

    /// <summary>A new mark this close to the last one, in tiles, strengthens it instead of making another.</summary>
    [DataField]
    public float MergeRadius = 1f;

    /// <summary>Seconds between marks, give or take <see cref="EmitIntervalVariance"/> of itself.</summary>
    [DataField]
    public float EmitInterval = 1.5f;

    [DataField]
    public float EmitIntervalVariance = 0.3f;

    [DataField]
    public float MinEmitInterval = 0.1f;

    /// <summary>How much a merge adds to a mark's strength, up to 1.</summary>
    [DataField]
    public float MergeStrengthStep = 0.25f;

    /// <summary>How long a mark lasts, in seconds. A trail is five minutes of walking, as PF2e's Track expects.</summary>
    [DataField]
    public float DecayTime = 300f;

    public TimeSpan NextEmitTime;

    /// <summary>The newest mark of this trail. Only it is merged into, so revisiting a spot does not rewrite the order.</summary>
    public EntityUid? LastMarkerEntity;
}
