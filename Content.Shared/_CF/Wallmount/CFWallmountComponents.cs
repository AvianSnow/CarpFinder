// Ported from CrystallPunk 14 (CP14WallmountComponent, CP14WallmountedComponent).
namespace Content.Shared.CF.Wallmount;

/// <summary>
/// Attaches the entity to the wall behind it shortly after it appears. If no wall is found, the entity is deleted.
/// </summary>
[RegisterComponent, Access(typeof(CFWallmountSystem))]
public sealed partial class CFWallmountComponent : Component
{
    [DataField]
    public int AttachAttempts = 3;

    [DataField]
    public TimeSpan NextAttachTime = TimeSpan.Zero;
}

/// <summary>
/// On a wall: the entities attached to it. Destroying or unanchoring the wall destroys them.
/// </summary>
[RegisterComponent, Access(typeof(CFWallmountSystem))]
public sealed partial class CFWallmountedComponent : Component
{
    [DataField]
    public HashSet<EntityUid> Attached = new();
}
