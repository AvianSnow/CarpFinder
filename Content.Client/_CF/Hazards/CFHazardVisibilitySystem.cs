using Content.Shared.CF.Hazards;
using Content.Shared.Ghost.Components;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client.CF.Hazards;

/// <summary>
/// A hazard is drawn only for players who can see it: it went off, they noticed it, or it belongs to their side.
/// Ghosts and anyone without a body (mappers) always see hazards.
/// </summary>
public sealed partial class CFHazardVisibilitySystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private CFHazardSystem _hazards = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var viewer = _player.LocalEntity;
        var seesAll = viewer == null || HasComp<GhostComponent>(viewer);

        var query = EntityQueryEnumerator<CFHazardComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var hazard, out var sprite))
        {
            var visible = seesAll || _hazards.CanSee((uid, hazard), viewer!.Value);
            if (sprite.Visible != visible)
                _sprite.SetVisible((uid, sprite), visible);
        }
    }
}
