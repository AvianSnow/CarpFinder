namespace Content.Shared.CF.Actions;

/// <summary>
/// Raised on a creature when its action pool fills. That is the end of its turn:
/// frightened eases, a raised shield drops, and reactions come back.
/// </summary>
public sealed class CFTurnRefilledEvent : EntityEventArgs
{
}
