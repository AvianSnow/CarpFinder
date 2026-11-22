using Content.Shared.CF.Effects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client.CF.Effects;

/// <summary>
/// Draws a glowing ring where an active aura ends, so everyone can see it. The ring sits on the owner's drawn position,
/// so it glides with them instead of jumping from tile to tile, and its radius is <see cref="CFAuraSystem.Radius"/>,
/// the same circle the server counts creatures in.
/// </summary>
public sealed partial class CFAuraOverlaySystem : EntitySystem
{
    private static readonly Color BorderColor = Color.FromHex("#90C8FF");

    /// <summary>How far inward each pass of the ring is drawn, in tiles. Lines are one pixel wide, so this thickens it.</summary>
    private static readonly float[] Passes = { 0f, 0.03f, 0.06f };

    /// <summary>How strongly the inside of the ring is tinted, on top of the pulse.</summary>
    private const float FillAlpha = 0.05f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new AuraOverlay(this));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<AuraOverlay>();
    }

    private void Draw(in OverlayDrawArgs args)
    {
        var pulse = 0.75f + 0.25f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 3f);
        var color = BorderColor.WithAlpha(pulse);
        var fill = BorderColor.WithAlpha(FillAlpha * pulse);
        var handle = args.WorldHandle;

        var query = EntityQueryEnumerator<CFAuraComponent, TransformComponent>();
        while (query.MoveNext(out _, out var aura, out var xform))
        {
            if (!aura.Active || xform.MapID != args.MapId)
                continue;

            var center = _transform.GetWorldPosition(xform);
            foreach (var size in aura.ActiveSizes)
            {
                var radius = CFAuraSystem.Radius(size);
                if (!args.WorldAABB.Enlarged(radius).Contains(center))
                    continue;

                handle.DrawCircle(center, radius, fill);
                foreach (var pass in Passes)
                {
                    handle.DrawCircle(center, radius - pass, color, filled: false);
                }
            }
        }
    }

    private sealed class AuraOverlay : Overlay
    {
        private readonly CFAuraOverlaySystem _system;
        public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

        public AuraOverlay(CFAuraOverlaySystem system)
        {
            _system = system;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            _system.Draw(args);
        }
    }
}
