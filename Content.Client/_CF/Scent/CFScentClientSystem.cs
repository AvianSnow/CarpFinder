// Ported from Starlight (https://github.com/ss14Starlight/space-station-14), their Scent system, with permission.
// Cut down for CarpFinder: one kind of smeller, no tracking of one scent, no overlay through blindness.

using System.Globalization;
using System.Numerics;
using Content.Shared.CF.Scent;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Animations;
using Robust.Shared.Timing;

namespace Content.Client.CF.Scent;

/// <summary>
/// Draws the scent trails a sniffing smeller is sent: each mark in its scent's colour, fading as it ages, and hidden from
/// anyone without the sense, such as a replay.
/// </summary>
public sealed partial class CFScentClientSystem : CFSharedScentSystem
{
    private const string FadeAnimationKey = "cf-scent-fade";

    private const float MinAlpha = 85f / 255f;
    private const float MaxAlpha = 127f / 255f;

    /// <summary>The marker sprite is drawn at a third of a tile when fresh, and a bit over a tile when pooled.</summary>
    private const float MinScale = 32f / 96f;
    private const float MaxScale = 1.25f;

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private AnimationPlayerSystem _animation = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CFSmellerComponent, AfterAutoHandleStateEvent>(OnSmellerState);
        SubscribeLocalEvent<CFScentMarkerComponent, ComponentStartup>(OnMarkerStartup);
        SubscribeLocalEvent<CFScentMarkerComponent, AfterAutoHandleStateEvent>(OnMarkerState);
    }

    private void OnSmellerState(Entity<CFSmellerComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        var query = EntityQueryEnumerator<CFScentMarkerComponent>();
        while (query.MoveNext(out var uid, out var marker))
        {
            ApplyFilter((uid, marker));
        }
    }

    private void OnMarkerStartup(Entity<CFScentMarkerComponent> ent, ref ComponentStartup args)
    {
        PlayFade(ent);
        ApplyFilter(ent);
    }

    /// <summary>A mark's state arrives again when it is merged into or re-enters view, so both are redone.</summary>
    private void OnMarkerState(Entity<CFScentMarkerComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        PlayFade(ent);
        ApplyFilter(ent);
    }

    private void ApplyFilter(Entity<CFScentMarkerComponent> ent)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        var visible = _player.LocalEntity is { } local
                      && TryComp(local, out CFSmellerComponent? smeller)
                      && smeller.Sniffing;
        _sprite.SetVisible((ent.Owner, sprite), visible);
    }

    private void PlayFade(Entity<CFScentMarkerComponent> ent)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        // Play throws on a key already playing, so stop first.
        _animation.Stop(ent.Owner, null, FadeAnimationKey);

        var strength = Math.Clamp(ent.Comp.Strength, 0f, 1f);
        var scale = MathHelper.Lerp(MinScale, MaxScale, strength);
        _sprite.SetScale((ent.Owner, sprite), new Vector2(scale, scale));

        var startColor = ScentColor(ent.Comp.ScentId).WithAlpha(MathHelper.Lerp(MinAlpha, MaxAlpha, strength));
        var endColor = Color.Gray.WithAlpha(0f);
        var remaining = MathF.Max(0.1f, (float) (ent.Comp.ExpiresAt - _timing.CurTime).TotalSeconds);
        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(remaining),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startColor, 0f),
                        new AnimationTrackProperty.KeyFrame(endColor, remaining),
                    },
                },
            },
        };
        _animation.Play(ent.Owner, animation, FadeAnimationKey);
    }

    /// <summary>Each scent gets a hue from its id, so two trails can be told apart. Convert.ToUInt32 is not allowed in the sandbox.</summary>
    public static Color ScentColor(string scentId)
    {
        if (scentId.Length < 8)
            return Color.White;

        var seed = uint.Parse(scentId[..8], NumberStyles.HexNumber);
        var hue = seed % 360 / 360f;
        return Color.FromHsv(new Vector4(hue, 0.85f, 1f, 1f));
    }
}
