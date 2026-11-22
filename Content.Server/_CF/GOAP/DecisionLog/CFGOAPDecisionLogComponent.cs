using Content.Server.CF.GOAP.Steering;
using Content.Shared.CF.GOAP;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.GOAP.DecisionLog;

/// <summary>
/// FOR DEBUGGING ONLY. This will be removed once the AI has settled: <c>.agent/goap.md</c> lists the steps.
/// A running list of what one brain decided, newest last, readable in VV. Every brain gets one.
/// </summary>
/// <remarks>Do not call this folder Debug. <c>.gitignore</c> drops any folder with that name, so it would never be pushed.</remarks>
[RegisterComponent]
public sealed partial class CFGOAPDecisionLogComponent : Component
{
    /// <summary>The oldest entries drop off past this, so it stays short enough to read.</summary>
    public const int MaxEntries = 64;

    [ViewVariables]
    public List<string> Entries = new();

    // What it last wrote down. The system compares these against the brain to see what changed.

    public ProtoId<CFGOAPGoalPrototype>? Goal;
    public string Plan = string.Empty;
    public int Step;
    public bool StepStarted;
    public ulong Facts;
    public EntityUid? Target;
    public EntityUid? Node;
    public TimeSpan ForcedUntil;
    public TimeSpan CorneredUntil;
    public TimeSpan HurtByUntil;
    public EntityUid? Obstacle;
    public CFGOAPFlowField? ObstacleField;
    public bool Working;
    public TimeSpan NoWayUntil;
    public int Route;
}
