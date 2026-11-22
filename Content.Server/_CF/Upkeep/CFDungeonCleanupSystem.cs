using Content.Server.Chat.Systems;
using Content.Shared.CF.Harvest;
using Content.Shared.CF.Shop;
using Content.Shared.Fluids.Components;
using Content.Shared.Item;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server.CF.Upkeep;

/// <summary>
/// The dungeon wizard's cleanup. On a timer, after warnings, it deletes loose items, puddles and dead bodies on every
/// running map. It keeps containers and what is in them, living creatures, bodies a player can still return to,
/// coins and treasure, anchored things, and anything the mapper placed.
/// </summary>
public sealed partial class CFDungeonCleanupSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFPlayerBodySystem _bodies = default!;

    private static readonly Color AnnouncementColor = Color.MediumPurple;

    private readonly HashSet<EntityUid> _maps = new();
    private readonly List<EntityUid> _delete = new();
    private readonly List<EntityUid> _release = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFDungeonCleanupComponent>();
        while (query.MoveNext(out _, out var cleanup))
        {
            if (cleanup.NextCleanup is not { } next)
            {
                cleanup.NextCleanup = now + cleanup.Interval;
                cleanup.WarningsGiven = 0;
                continue;
            }

            var due = DueWarning(cleanup, now);
            if (due >= cleanup.WarningsGiven)
            {
                Announce(next - now);
                cleanup.WarningsGiven = due + 1;
            }

            if (now < next)
                continue;

            var cleaned = CleanAll(cleanup.MapperGrace);
            _chat.DispatchGlobalAnnouncement(Loc.GetString("cf-cleanup-done", ("count", cleaned)),
                Loc.GetString("cf-cleanup-sender"), colorOverride: AnnouncementColor);
            cleanup.NextCleanup = now + cleanup.Interval;
            cleanup.WarningsGiven = 0;
        }
    }

    /// <summary>The index of the latest warning whose time has come, or -1. Earlier ones it passed are skipped.</summary>
    private static int DueWarning(CFDungeonCleanupComponent cleanup, TimeSpan now)
    {
        var due = -1;
        for (var i = 0; i < cleanup.Warnings.Count; i++)
        {
            if (now >= cleanup.NextCleanup - cleanup.Warnings[i])
                due = i;
        }

        return due;
    }

    private void Announce(TimeSpan left)
    {
        var message = left.TotalSeconds >= 60
            ? Loc.GetString("cf-cleanup-warning-minutes", ("minutes", (int) Math.Round(left.TotalMinutes)))
            : Loc.GetString("cf-cleanup-warning-seconds", ("seconds", (int) Math.Ceiling(left.TotalSeconds)));
        _chat.DispatchGlobalAnnouncement(message, Loc.GetString("cf-cleanup-sender"), colorOverride: AnnouncementColor);
    }

    /// <summary>Cleans every map that is not paused. Returns how many things were removed.</summary>
    public int CleanAll(TimeSpan mapperGrace)
    {
        _maps.Clear();
        var maps = EntityQueryEnumerator<MapComponent>();
        while (maps.MoveNext(out var uid, out _))
        {
            if (!_map.IsPaused(uid))
                _maps.Add(uid);
        }

        return Clean(_maps, mapperGrace);
    }

    /// <summary>Cleans the given maps. Returns how many things were removed.</summary>
    public int Clean(IReadOnlySet<EntityUid> maps, TimeSpan mapperGrace)
    {
        _delete.Clear();
        _release.Clear();
        var graceTicks = (uint) Math.Ceiling(mapperGrace.TotalSeconds * _timing.TickRate);

        var puddles = EntityQueryEnumerator<PuddleComponent, TransformComponent, MetaDataComponent>();
        while (puddles.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            // Puddles are anchored to their tile, so they skip the anchored rule.
            if (Cleanable(uid, xform, meta, maps, graceTicks, anchoredStays: false))
                _delete.Add(uid);
        }

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent, MetaDataComponent>();
        while (mobs.MoveNext(out var uid, out var state, out var xform, out var meta))
        {
            if (!_mobState.IsDead(uid, state)
                || !Cleanable(uid, xform, meta, maps, graceTicks)
                || _bodies.PlayerCanReturn(uid))
                continue;

            if (HasComp<CFPlayerBodyComponent>(uid))
                _release.Add(uid);
            else
                _delete.Add(uid);
        }

        var items = EntityQueryEnumerator<ItemComponent, TransformComponent, MetaDataComponent>();
        while (items.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (KeptItem(uid) || !Cleanable(uid, xform, meta, maps, graceTicks))
                continue;

            _delete.Add(uid);
        }

        // Belongings first, so a body's bag lands on the floor before the body goes.
        foreach (var body in _release)
            _bodies.ReleaseBody(body);

        foreach (var uid in _delete)
            QueueDel(uid);

        return _delete.Count + _release.Count;
    }

    /// <summary>
    /// On a map being cleaned, loose on the floor, not anchored, and not placed by the mapper.
    /// </summary>
    private bool Cleanable(EntityUid uid,
        TransformComponent xform,
        MetaDataComponent meta,
        IReadOnlySet<EntityUid> maps,
        uint graceTicks,
        bool anchoredStays = true)
    {
        if (xform.MapUid is not { } map || !maps.Contains(map) || anchoredStays && xform.Anchored)
            return false;

        if (_container.IsEntityOrParentInContainer(uid, meta, xform))
            return false;

        var mapCreated = MetaData(map).CreationTick.Value;
        return meta.CreationTick.Value > mapCreated + graceTicks;
    }

    /// <summary>Items the wizard never takes: containers, coins, treasure, living or minded things.</summary>
    private bool KeptItem(EntityUid uid)
    {
        if (HasComp<StorageComponent>(uid) || HasComp<EntityStorageComponent>(uid))
            return true;

        if (HasComp<CFCoinComponent>(uid))
            return true;

        if (TryComp<CFItemPriceComponent>(uid, out var price) && price.FullResale || HasComp<CFPartsValueComponent>(uid))
            return true;

        if (HasComp<MobStateComponent>(uid))
            return true;

        return TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind;
    }
}
