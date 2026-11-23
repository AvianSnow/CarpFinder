// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Analyzers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.CF.ZLevels.Core;

public sealed partial class CFZLevelsSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ViewSubscriberSystem _viewSubscriber = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private readonly EntProtoId _zEyeProto = "CFZLevelEye";

    private readonly TimeSpan _zLevelViewerUpdateRate = TimeSpan.FromSeconds(1f);
    private TimeSpan _nextZLevelViewerUpdate = TimeSpan.Zero;

    private void InitView()
    {
    }

    /// <summary>
    /// A viewer's eye stack is built from the maps surrounding it, so it goes stale the moment
    /// the network gains or loses a level. Rebuild it for everyone inside that network.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMapNetworkUpdated(Entity<CFZMapNetworkComponent> ent, ref CFZLevelMapNetworkUpdatedEvent args)
    {
        var query = EntityQueryEnumerator<CFZLevelViewerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var viewer, out var xform))
        {
            if (xform.MapUid is not { } mapUid ||
                !TryComp<CFZMapComponent>(mapUid, out var zMap) ||
                zMap.NetworkUid != ent.Owner)
                continue;

            UpdateViewer((uid, viewer));
        }
    }

    private void UpdateView(float frameTime)
    {
        if (_timing.CurTime < _nextZLevelViewerUpdate)
            return;
        _nextZLevelViewerUpdate = _timing.CurTime + _zLevelViewerUpdateRate;

        var query = EntityQueryEnumerator<CFZLevelViewerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var viewer, out var xform))
        {
            var worldPosition = _transform.GetWorldPosition(xform);

            foreach (var eye in viewer.Eyes)
            {
                if (TerminatingOrDeleted(eye))
                    continue;

                _transform.SetWorldPosition(eye, worldPosition);
            }
        }
    }

    [SubscribeLocalEvent]
    private void OnViewerInit(Entity<CFZLevelViewerComponent> ent, ref MapInitEvent args)
    {
        // CarpFinder: a body with an alerts panel gets the Look up button there; a ghost keeps the action.
        if (HasComp<AlertsComponent>(ent))
            RefreshLookAlert(ent);
        else
            _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.ActionId);

        _meta.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }

    [SubscribeLocalEvent]
    private void OnCompRemove(Entity<CFZLevelViewerComponent> ent, ref ComponentRemove args)
    {
        _actions.RemoveAction(ent.Comp.ActionEntity);
        ClearLookAlert(ent); // CarpFinder
        _meta.RemoveFlag(ent, MetaDataFlags.ExtraTransformEvents);

        foreach (var eye in ent.Comp.Eyes)
        {
            QueueDel(eye);
        }
    }

    [SubscribeLocalEvent]
    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        var viewer = EnsureComp<CFZLevelViewerComponent>(ev.Entity);
        UpdateViewer((ev.Entity, viewer));
    }

    [SubscribeLocalEvent]
    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        RemComp<CFZLevelViewerComponent>(ev.Entity);
    }

    [SubscribeLocalEvent]
    private void OnViewerMapUidChanged(Entity<CFZLevelViewerComponent> ent, ref MapUidChangedEvent args)
    {
        UpdateViewer(ent);
    }

    private void UpdateViewer(Entity<CFZLevelViewerComponent> ent)
    {
        var eyes = ent.Comp.Eyes;
        foreach (var eye in ent.Comp.Eyes)
        {
            if (!TerminatingOrDeleted(eye))
                QueueDel(eye);
        }
        eyes.Clear();

        RefreshLookAlert(ent); // CarpFinder: the level above may have come or gone

        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        var xform = Transform(ent);
        var map = xform.MapUid;

        if (map is null)
            return;

        var globalPos = _transform.GetWorldPosition(xform);

        for (var i = 1; i <= MaxZLevelsBelowRendering; i++)
        {
            if (!TryMapOffset(map.Value, -i, out var mapUidBelow))
                break;

            var newEye = SpawnAtPosition(_zEyeProto, new EntityCoordinates(mapUidBelow, globalPos));

            Transform(newEye).GridTraversal = false;
            _viewSubscriber.AddViewSubscriber(newEye, actor.PlayerSession);
            eyes.Add(newEye);
        }

        // We constantly load the upper z-levels for the client so that you can quickly look up and climb stairs without PVS lag.
        for (var i = 1; i <= MaxZLevelsAboveRendering; i++)
        {
            if (!TryMapOffset(map.Value, i, out var mapUidAbove))
                break;

            var newEye = SpawnAtPosition(_zEyeProto, new EntityCoordinates(mapUidAbove, globalPos));

            Transform(newEye).GridTraversal = false;
            _viewSubscriber.AddViewSubscriber(newEye, actor.PlayerSession);
            eyes.Add(newEye);
        }
    }
}
