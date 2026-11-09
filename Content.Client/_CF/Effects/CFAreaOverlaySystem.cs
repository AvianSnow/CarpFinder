using Content.Shared.CF.Spells;
using System.Numerics;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Effects.Areas;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client.CF.Effects;

/// <summary>
/// While an action with <see cref="CFAreaEffectComponent"/> is aimed, draws the tiles it will cover.
/// Green for areas that only help allies, orange for anything that can hit enemies.
/// </summary>
public sealed partial class CFAreaOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private CFSpellshapeSystem _spellshapes = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    private ActionUIController? _actions;

    public override void Initialize()
    {
        base.Initialize();
        _actions = _ui.GetUIController<ActionUIController>();
        _overlays.AddOverlay(new AreaOverlay(this));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<AreaOverlay>();
    }

    private void Draw(in OverlayDrawArgs args)
    {
        if (_actions?.SelectingTargetFor is not { } action || !TryComp(action, out CFAreaEffectComponent? area))
            return;

        var player = _player.LocalEntity;
        if (player == null || !TryComp(player, out TransformComponent? xform))
            return;

        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouse.MapId != args.MapId)
            return;

        var casterMap = _transform.GetMapCoordinates(player.Value, xform);
        if (casterMap.MapId != mouse.MapId || !_maps.TryFindGridAt(casterMap, out var grid, out var gridComp))
            return;

        var tiles = CFAreaGrid.Collect(
            area.Shape,
            area.SizeFeet + _spellshapes.ExtraAreaFeet(action, player.Value, area),
            _maps.WorldToLocal(grid, gridComp, casterMap.Position),
            _maps.WorldToLocal(grid, gridComp, mouse.Position),
            gridComp.TileSize,
            area.IncludeSelf);

        var color = area.Filter == CFTargetFilter.Allies
            ? new Color(80, 220, 120, 220)
            : new Color(255, 120, 40, 220);

        foreach (var tile in tiles)
        {
            var center = _maps.LocalToWorld(grid, gridComp, _maps.TileCenterToVector(grid, gridComp, tile));
            var half = gridComp.TileSize * 0.46f;
            var box = new Box2(center - new Vector2(half, half), center + new Vector2(half, half));
            var bl = box.BottomLeft;
            var tr = box.TopRight;
            var br = new Vector2(tr.X, bl.Y);
            var tl = new Vector2(bl.X, tr.Y);
            args.WorldHandle.DrawRect(box, color.WithAlpha(0.22f));
            args.WorldHandle.DrawLine(bl, br, color);
            args.WorldHandle.DrawLine(br, tr, color);
            args.WorldHandle.DrawLine(tr, tl, color);
            args.WorldHandle.DrawLine(tl, bl, color);
        }
    }

    private sealed class AreaOverlay : Overlay
    {
        private readonly CFAreaOverlaySystem _system;
        public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

        public AreaOverlay(CFAreaOverlaySystem system)
        {
            _system = system;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            _system.Draw(args);
        }
    }
}
