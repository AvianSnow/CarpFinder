using Content.Shared.CF.Actions;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client.CF.Actions;

/// <summary>Spins a tumbler's sprite one full turn, so everyone can see the action happen.</summary>
public sealed partial class CFTumbleVisualsSystem : EntitySystem
{
    private const string AnimationKey = "cf_tumble_spin";

    private const float SpinSeconds = 0.5f;

    private static readonly Animation Spin = new()
    {
        Length = TimeSpan.FromSeconds(SpinSeconds),
        AnimationTracks =
        {
            new AnimationTrackComponentProperty
            {
                ComponentType = typeof(SpriteComponent),
                InterpolationMode = AnimationInterpolationMode.Linear,
                Property = nameof(SpriteComponent.Rotation),
                // Thirds, because a half turn could interpolate either way round.
                KeyFrames =
                {
                    new AnimationTrackProperty.KeyFrame(Angle.Zero, 0f),
                    new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(120), SpinSeconds / 3f),
                    new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(240), SpinSeconds / 3f),
                    new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(360), SpinSeconds / 3f),
                },
            },
        },
    };

    [Dependency] private AnimationPlayerSystem _animations = default!;

    [SubscribeNetworkEvent]
    private void OnSpin(CFTumbleSpinEvent ev)
    {
        if (!TryGetEntity(ev.Tumbler, out var tumbler) || _animations.HasRunningAnimation(tumbler.Value, AnimationKey))
            return;

        _animations.Play(tumbler.Value, Spin, AnimationKey);
    }
}
