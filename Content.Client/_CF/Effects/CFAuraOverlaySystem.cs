using System.Numerics;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Effects.Areas;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client.CF.Effects;

/// <summary>Draws a glowing line around the tiles an active aura covers, so everyone can see where it ends.</summary>
public sealed partial class CFAuraOverlaySystem : EntitySystem
{
    private static readonly Color BorderColor = Color.FromHex("#90C8FF");

    /// <summary>How far inward each pass of the line is drawn, in tiles. Lines are one pixel wide, so this thickens it.</summary>
    private static readonly float[] Passes = { 0f, 0.03f, 0.06f };

    private static readonly (Vector2i Neighbour, Vector2 From, Vector2 To)[] Edges =
    {
        (new Vector2i(0, -1), new Vector2(0, 0), new Vector2(1, 0)),
        (new Vector2i(1, 0), new Vector2(1, 0), new Vector2(1, 1)),
        (new Vector2i(0, 1), new Vector2(1, 1), new Vector2(0, 1)),
        (new Vector2i(-1, 0), new Vector2(0, 1), new Vector2(0, 0)),
    };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private SharedMapSystem _maps = default!;
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

        var query = EntityQueryEnumerator<CFAuraComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var aura, out var xform))
        {
            if (!aura.Active || xform.MapID != args.MapId)
                continue;

            var origin = _transform.GetMapCoordinates(uid, xform);
            if (!_maps.TryFindGridAt(origin, out var grid, out var gridComp))
                continue;

            var local = _maps.WorldToLocal(grid, gridComp, origin.Position);
            var tiles = CFAreaGrid.Collect(CFAreaShape.Emanation, aura.SizeFeet, local, local, gridComp.TileSize, true);
            DrawBorder(args.WorldHandle, grid, gridComp, tiles, color);
        }
    }

    /// <summary>Only an edge with no aura tile on its far side is on the border.</summary>
    private void DrawBorder(DrawingHandleWorld handle, EntityUid grid, MapGridComponent gridComp, HashSet<Vector2i> tiles, Color color)
    {
        var size = gridComp.TileSize;
        foreach (var tile in tiles)
        {
            foreach (var (neighbour, from, to) in Edges)
            {
                if (tiles.Contains(tile + neighbour))
                    continue;

                var corner = new Vector2(tile.X, tile.Y);
                var inward = new Vector2(-neighbour.X, -neighbour.Y);
                foreach (var pass in Passes)
                {
                    var start = _maps.LocalToWorld(grid, gridComp, (corner + from + inward * pass) * size);
                    var end = _maps.LocalToWorld(grid, gridComp, (corner + to + inward * pass) * size);
                    handle.DrawLine(start, end, color);
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
