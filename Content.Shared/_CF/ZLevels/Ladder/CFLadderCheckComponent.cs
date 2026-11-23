namespace Content.Shared.CF.ZLevels.Ladder;

/// <summary>
/// Ours, beside CrystallEdge's <see cref="CFLadderComponent"/>: the Player Core Climb check. With a DC, taking hold
/// of the ladder is an Athletics check against it and costs an action, as Climb does. A failure from the ground
/// makes no progress, and a critical failure leaves the climber prone, or lets go on the way down. A DC of 0 is a
/// trivial climb: no check and no cost, as the rules allow. <c>CFGrantClimb</c> helps: a bonus, a success that
/// counts as critical, or a climb Speed that needs no check. The server's <c>CFLadderCheckSystem</c> runs it.
/// </summary>
[RegisterComponent]
public sealed partial class CFLadderCheckComponent : Component
{
    [DataField]
    public int Dc;

    /// <summary>What a checked climb costs.</summary>
    [DataField]
    public int Actions = 1;

    /// <summary>A checked climb needs both hands.</summary>
    [DataField]
    public bool NeedsFreeHands = true;

    /// <summary>How long a verdict holds, so touching the ladder again does not roll or pay again at once.</summary>
    [DataField]
    public float RetrySeconds = 6f;

    /// <summary>Server only: the last verdict for each creature, and until when it holds.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, (TimeSpan Until, bool Allowed)> Verdicts = new();
}

/// <summary>Raised on a ladder as a creature takes hold of it. Cancel it and the creature does not.</summary>
[ByRefEvent]
public record struct CFLadderGripAttemptEvent(EntityUid Climber, bool Cancelled = false);
