using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Cooking;

/// <summary>What the cooking steps share: swapping one thing for the next stage of it, where it is.</summary>
public sealed partial class CFCookingSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    /// <summary>
    /// Replaces <paramref name="old"/> with <paramref name="result"/> in the same place: the same hand or store if it
    /// was in one, otherwise on the floor where it lay. Server only; the client gets the result as a game state.
    /// </summary>
    public EntityUid? Replace(EntityUid old, EntProtoId result)
    {
        if (_net.IsClient || TerminatingOrDeleted(old))
            return null;

        EntityUid spawned;
        if (_containers.TryGetContainingContainer((old, null, null), out var container))
        {
            var owner = container.Owner;
            var id = container.ID;
            _containers.Remove(old, container, force: true);
            spawned = SpawnInContainerOrDrop(result, owner, id);
        }
        else
        {
            spawned = Spawn(result, Transform(old).Coordinates);
        }

        QueueDel(old);
        return spawned;
    }
}
