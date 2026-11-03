namespace Content.Shared.CF.Companions;

/// <summary>
/// Marker on a creature that has at least one <see cref="CFCompanionComponent"/> ally.
/// Pointing is read from this body, using that player's point key.
/// </summary>
[RegisterComponent]
public sealed partial class CFSummonerComponent : Component
{
}
