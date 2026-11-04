namespace Content.Shared.CF.Effects;

/// <summary>
/// Server-steered bolt. Each tick it flies toward the creature that was clicked,
/// instead of keeping the direction from the moment of the cast.
/// </summary>
[RegisterComponent]
public sealed partial class CFHomingProjectileComponent : Component
{
    [DataField]
    public EntityUid Target;

    [DataField]
    public EntityUid User;

    /// <summary>The spell action or item whose effect components resolve on arrival.</summary>
    [DataField]
    public EntityUid Source;

    [DataField]
    public bool Resolved;

    [DataField]
    public float Speed = 18f;
}
