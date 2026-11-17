// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Content.Shared.ActionBlocker;
using Content.Shared.Chasm;
using Robust.Shared.Analyzers;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.CF.ZLevels.Chasm;

public sealed partial class CFZLevelChasmSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;

    private static readonly SoundPathSpecifier FallingSound = new("/Audio/Effects/falling.ogg");

    [SubscribeLocalEvent]
    private void OnFallOutOfBounds(Entity<CFZLevelChasmComponent> ent, ref CFZLevelFallOutOfBoundsEvent args)
    {
        var player = args.Player;

        if (HasComp<ChasmFallingComponent>(player))
            return; //Already falling

        var attempt = new CFZLevelChasmAttempt(player);
        RaiseLocalEvent(player, attempt);

        if (attempt.Cancelled)
            return;

        _audio.PlayPredicted(FallingSound, Transform(player).Coordinates, player);
        var falling = AddComp<ChasmFallingComponent>(player);
        falling.NextDeletionTime = _timing.CurTime + falling.DeletionTime;
        _blocker.UpdateCanMove(player);
    }
}
