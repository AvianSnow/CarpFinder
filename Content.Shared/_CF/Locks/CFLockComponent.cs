using Robust.Shared.GameStates;

namespace Content.Shared.CF.Locks;

/// <summary>
/// Pathfinder rules for an entity with a stock <c>Lock</c> (door, chest, locker). The stock component holds
/// whether it is locked; this one holds how hard it is to get past.
/// Based on CP14's <c>CP14LockComponent</c>, with its key shapes replaced by Pick a Lock and Force Open.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFLockComponent : Component
{
    /// <summary>Thievery DC to Pick a Lock.</summary>
    [DataField, AutoNetworkedField]
    public int Dc = 20;

    /// <summary>Successes needed to open it. A critical success counts as two.</summary>
    [DataField, AutoNetworkedField]
    public int SuccessesNeeded = 3;

    /// <summary>Successes so far. They last until the lock opens.</summary>
    [DataField, AutoNetworkedField]
    public int Successes;

    /// <summary>Athletics DC to Force Open what the lock is on. It depends on the door, not the lock.</summary>
    [DataField, AutoNetworkedField]
    public int ForceDc = 20;

    /// <summary>The <see cref="CFKeyComponent.KeyId"/> that fits. Null means no key does.</summary>
    [DataField, AutoNetworkedField]
    public string? KeyId;

    /// <summary>Forced open. It stays unlocked and cannot be locked again.</summary>
    [DataField, AutoNetworkedField]
    public bool Broken;

    /// <summary>Picking left marks anyone can see on a close look.</summary>
    [DataField, AutoNetworkedField]
    public bool Tampered;

    /// <summary>Creatures that jammed it with a critical failure. They take −2 on later attempts to Force Open it.</summary>
    [DataField]
    public HashSet<EntityUid> JammedBy = new();
}

/// <summary>Opens and closes every <see cref="CFLockComponent"/> with the same <see cref="KeyId"/>.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFKeyComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public string KeyId = string.Empty;
}

/// <summary>Needed to Pick a Lock, held or worn. A critical failure breaks it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFThievesToolkitComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Broken;
}

/// <summary>Held while Forcing Open, this removes the −2 item penalty. A levered crowbar also gives +1.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFCrowbarComponent : Component
{
    [DataField, AutoNetworkedField]
    public int ItemBonus;
}
