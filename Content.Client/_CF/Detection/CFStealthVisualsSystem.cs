using Content.Shared.CF.Detection;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client.CF.Detection;

/// <summary>
/// Draws a sneaking creature as this player sees it: gone if undetected, faint if hidden.
/// The data reaches every client, so a modified client could still show it.
/// </summary>
public sealed partial class CFStealthVisualsSystem : EntitySystem
{
    private const float HiddenAlpha = 0.35f;

    /// <summary>What the sneaker sees of itself, so it can tell it is hiding.</summary>
    private const float SelfAlpha = 0.7f;

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private CFStealthSystem _stealth = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var viewer = _player.LocalEntity;
        var query = EntityQueryEnumerator<CFStealthComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var stealth, out var sprite))
        {
            var alpha = Alpha(uid, stealth, viewer);
            if (MathHelper.CloseTo(alpha, stealth.ClientAlpha))
                continue;

            stealth.ClientAlpha = alpha;
            _sprite.SetVisible((uid, sprite), alpha > 0f);
            _sprite.SetColor((uid, sprite), Color.White.WithAlpha(alpha));
        }
    }

    private float Alpha(EntityUid uid, CFStealthComponent stealth, EntityUid? viewer)
    {
        if (!stealth.Active || viewer == null)
            return 1f;

        if (uid == viewer)
            return SelfAlpha;

        return _stealth.Detection(uid, viewer.Value) switch
        {
            CFDetection.Undetected => 0f,
            CFDetection.Hidden => HiddenAlpha,
            _ => 1f,
        };
    }
}
