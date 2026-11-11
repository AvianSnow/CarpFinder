using Content.Server.Station.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.CF.Upkeep;

/// <summary>
/// Gives a job slot back when its player's body is deleted, removes bodies left without their player for too long,
/// and drops everything a body carried first so loot is not lost.
/// </summary>
public sealed partial class CFPlayerBodySystem : EntitySystem
{
    /// <summary>How often empty bodies are checked.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ServerStationJobsSystem _jobs = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    private TimeSpan _nextCheck;
    private readonly List<EntityUid> _toDrop = new();
    private readonly List<EntityUid> _abandoned = new();

    [SubscribeLocalEvent]
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var body = EnsureComp<CFPlayerBodyComponent>(args.Mob);
        body.Station = args.Station;
        body.Job = args.JobId;
        body.User = args.Player.UserId;
    }

    [SubscribeLocalEvent]
    private void OnTerminating(Entity<CFPlayerBodyComponent> ent, ref EntityTerminatingEvent args)
    {
        ReturnSlot(ent);
    }

    /// <summary>Gives the body's job slot back to its station, once.</summary>
    public void ReturnSlot(Entity<CFPlayerBodyComponent> ent)
    {
        if (ent.Comp.SlotReturned || ent.Comp.Station is not { } station || ent.Comp.Job is not { } job)
            return;

        ent.Comp.SlotReturned = true;
        if (TerminatingOrDeleted(station))
            return;

        _jobs.TryAdjustJobSlot(station, job, 1);
    }

    /// <summary>
    /// Drops everything the body wears and holds where it lies: its bag with what is in it, weapons, wallet, armor and
    /// clothes. Nothing it carried is lost when it goes.
    /// </summary>
    public void DropBelongings(EntityUid body)
    {
        var coords = _transform.GetMoverCoordinates(body);
        _toDrop.Clear();
        _toDrop.AddRange(_inventory.GetHandOrInventoryEntities((body, null, null)));

        foreach (var item in _toDrop)
        {
            if (HasComp<VirtualItemComponent>(item) || HasComp<AttachedClothingComponent>(item))
                continue;

            if (_container.TryGetContainingContainer((item, null, null), out var container))
                _container.Remove(item, container, force: true, destination: coords);
        }
    }

    /// <summary>Drops what the body carried, then deletes it, which gives its job slot back.</summary>
    public void ReleaseBody(EntityUid body)
    {
        if (TerminatingOrDeleted(body))
            return;

        DropBelongings(body);
        QueueDel(body);
    }

    /// <summary>Releases every body this player spawned into. Respawning uses it.</summary>
    public int ReleaseBodiesOf(NetUserId user)
    {
        _abandoned.Clear();
        var query = EntityQueryEnumerator<CFPlayerBodyComponent>();
        while (query.MoveNext(out var uid, out var body))
        {
            if (body.User == user)
                _abandoned.Add(uid);
        }

        foreach (var uid in _abandoned)
        {
            ReleaseBody(uid);
        }

        return _abandoned.Count;
    }

    /// <summary>True when a player's mind still belongs to this body, so they can come back to it.</summary>
    public bool PlayerCanReturn(EntityUid body)
    {
        return _mind.TryGetMind(body, out _, out var mind) && mind.UserId != null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;
        _abandoned.Clear();

        var query = EntityQueryEnumerator<CFPlayerBodyComponent>();
        while (query.MoveNext(out var uid, out var body))
        {
            if (HasComp<ActorComponent>(uid))
            {
                body.LeftAt = null;
                continue;
            }

            body.LeftAt ??= now;
            if (now - body.LeftAt.Value >= body.SsdLimit)
                _abandoned.Add(uid);
        }

        foreach (var uid in _abandoned)
        {
            Log.Info($"Removing {ToPrettyString(uid)}, left by its player for too long.");
            ReleaseBody(uid);
        }
    }
}
