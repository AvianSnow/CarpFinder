namespace Content.Shared.CF.Actions;

/// <summary>Raised on a creature whenever its action pool is charged for something.</summary>
[ByRefEvent]
public readonly record struct CFActionsSpentEvent(int Actions);
