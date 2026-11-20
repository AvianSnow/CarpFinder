using Content.Shared.CF.Checks;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Raised on the weapon when a Strike with it has been rolled and its damage dealt, whatever the degree.
/// A bomb breaks and splashes on it.
/// </summary>
[ByRefEvent]
public readonly record struct CFStrikeResolvedEvent(EntityUid User, EntityUid Target, CFDegree Degree, int Damage, bool Melee);
