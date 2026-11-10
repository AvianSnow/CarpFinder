namespace Content.Shared.CF.Combat;

/// <summary>Raised on a creature whose Strike was a critical success against a creature, after its damage.</summary>
[ByRefEvent]
public readonly record struct CFLandedCriticalEvent(EntityUid Target);
