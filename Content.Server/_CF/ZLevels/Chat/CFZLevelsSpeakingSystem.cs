// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using System.Numerics;
using Robust.Shared.Analyzers;
using Content.Server.Chat.Systems;
using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server.CF.ZLevels.Chat;

public sealed partial class CFZLevelsSpeakingSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private CFSharedZLevelsSystem _zLevel = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<MapComponent> _mapQuery;

    private const float TransmitterLifetime = 3f;
    private const int MessageDelayMilliseconds = 333;

    public override void Initialize()
    {
        base.Initialize();

        _mapQuery = GetEntityQuery<MapComponent>();
    }

    [SubscribeLocalEvent]
    private void OnSpoke(Entity<CFZLevelViewerComponent> ent, ref EntitySpokeEvent args)
    {
        var xform = Transform(ent);
        var sourceMap = xform.MapUid;
        if (sourceMap is null)
            return;

        if (args.ObfuscatedMessage is not null) //Curse of chatcode: this is only way detect whispers
            return;

        var globalPosition = _transform.GetWorldPosition(xform);
        var message = args.Message;

        //Try transmit message to 1 zlevel down
        if (_zLevel.TryMapDown(sourceMap.Value, out var belowMapUid) &&
            _mapQuery.TryComp(belowMapUid, out var belowMapComp))
        {
            TransmitMessageToZLevel(
                belowMapComp,
                globalPosition,
                message,
                Loc.GetString("cf-zlevel-voice-from-up", ("name", Identity.Name(ent, EntityManager))));
        }

        //Try transmit message to 1 zlevel up
        if (_zLevel.TryMapUp(sourceMap.Value, out var aboveMapUid) &&
            _mapQuery.TryComp(aboveMapUid, out var aboveMapComp))
        {
            TransmitMessageToZLevel(
                aboveMapComp,
                globalPosition,
                message,
                Loc.GetString("cf-zlevel-voice-from-down", ("name", Identity.Name(ent, EntityManager))));
        }
    }

    private void TransmitMessageToZLevel(MapComponent mapComp, Vector2 position, string message, string nameOverride)
    {
        var targetPos = new MapCoordinates(position, mapComp.MapId);
        var transmit = Spawn(null, targetPos);
        EnsureComp<TimedDespawnComponent>(transmit).Lifetime = TransmitterLifetime;

        //It's not the most elegant solution, but as far as I understand, the entity doesn't have time to enter
        //the client's PVS after spawning, and we already start communicating through it. A slight delay solves the problem.
        Timer.Spawn(MessageDelayMilliseconds,
            () =>
            {
                //Map or transmitter may be gone by now (round restart, z-level teardown)
                if (TerminatingOrDeleted(transmit))
                    return;

                _chat.TrySendInGameICMessage(
                    transmit,
                    message,
                    InGameICChatType.Whisper,
                    false,
                    nameOverride: nameOverride,
                    ignoreActionBlocker: true);
            });
    }
}
