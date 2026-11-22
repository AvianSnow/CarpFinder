using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Shared.CF.Magic;

/// <summary>An illusion. The client tints it, and touching it makes it vanish.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CFIllusionComponent : Component;

/// <summary>Raised by Sigil for the server, which paints the rune.</summary>
[ByRefEvent]
public readonly record struct CFPlaceSigilEvent(EntityUid Caster, EntityCoordinates Spot);

/// <summary>
/// Figment: a real item spawned as an image, which cannot be picked up. It is picked from the shop's own items, so it is
/// something an adventurer might believe, and never a container that would spawn its contents.
/// </summary>
public sealed partial class CFFigmentSystem : EntitySystem
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly string[] Excluded = { "Storage", "ContainerFill", "EntityTableContainerFill", "StorageFill" };

    private List<EntProtoId>? _pool;

    public EntityUid? Conjure(EntityUid caster, EntityCoordinates spot)
    {
        if (!_net.IsServer)
            return null;

        var pool = Pool();
        if (pool.Count == 0)
            return null;

        var image = SpawnAtPosition(_random.Pick(pool), spot);
        EnsureComp<CFIllusionComponent>(image);
        EnsureComp<TimedDespawnComponent>(image).Lifetime = (float) Lifetime.TotalSeconds;
        return image;
    }

    /// <summary>Every item the shop sells that is not a container.</summary>
    public List<EntProtoId> Pool()
    {
        if (_pool != null)
            return _pool;

        _pool = new List<EntProtoId>();
        foreach (var proto in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract
                || !proto.ID.StartsWith("CF", StringComparison.Ordinal)
                || !proto.Components.ContainsKey("Item")
                || !proto.Components.ContainsKey("CFItemPrice"))
            {
                continue;
            }

            var container = false;
            foreach (var name in Excluded)
            {
                container |= proto.Components.ContainsKey(name);
            }

            if (!container)
                _pool.Add(proto.ID);
        }

        return _pool;
    }

    [SubscribeLocalEvent]
    private void OnPickupAttempt(Entity<CFIllusionComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        args.Cancel();
        Dispel(ent, args.User);
    }

    [SubscribeLocalEvent]
    private void OnInteractHand(Entity<CFIllusionComponent> ent, ref InteractHandEvent args)
    {
        args.Handled = true;
        Dispel(ent, args.User);
    }

    private void Dispel(EntityUid image, EntityUid toucher)
    {
        if (!_net.IsServer || TerminatingOrDeleted(image))
            return;

        _popup.PopupEntity(Loc.GetString("cf-figment-dispelled"), image, toucher);
        QueueDel(image);
    }
}
