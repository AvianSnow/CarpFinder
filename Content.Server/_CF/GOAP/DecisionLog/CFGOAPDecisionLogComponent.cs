using Content.Shared.CF.GOAP;
using Robust.Shared.Prototypes;

namespace Content.Server.CF.GOAP.DecisionLog;

/// <summary>
/// DEBUG, to be removed once the AI has settled. What a brain decided lately, newest last, for reading in VV.
/// The folder must not be called Debug: <c>.gitignore</c> drops any folder with that name, which lost the first copy.
/// </summary>
[RegisterComponent]
public sealed partial class CFGOAPDecisionLogComponent : Component
{
    /// <summary>The most entries it keeps.</summary>
    public const int MaxEntries = 64;

    [ViewVariables]
    public List<string> Entries = new();

    public ProtoId<CFGOAPGoalPrototype>? Goal;

    public string Plan = string.Empty;

    public ProtoId<CFGOAPActionPrototype>? Action;

    public ulong Facts;

    public EntityUid? Target;

    public ProtoId<CFGOAPGoalPrototype>? ForcedGoal;

    public EntityUid? Obstacle;

    public TimeSpan NoWayUntil;

    public int Route;
}
