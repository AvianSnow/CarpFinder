// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using System.Numerics;
using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Console;
using Robust.Shared.Enums;

namespace Content.Client.CF.ZLevels.Core.Overlays;

public sealed partial class CFZLevelDebugOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = null!;
    [Dependency] private IResourceCache _cache = null!;

    private readonly CFSharedZLevelsSystem _zLevels;
    private readonly SharedTransformSystem _transform;

    private readonly Font _font;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public CFZLevelDebugOverlay()
    {
        IoCManager.InjectDependencies(this);

        _zLevels = _entityManager.System<CFSharedZLevelsSystem>();
        _transform = _entityManager.System<SharedTransformSystem>();

        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 8);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        foreach (var uid in _zLevels.ActiveBodies)
        {
            if (!_entityManager.TryGetComponent<CFZPhysicsComponent>(uid, out var zPhys) ||
                !_entityManager.TryGetComponent<TransformComponent>(uid, out var xform))
                continue;

            if (!CFSharedZLevelsSystem.IsLoose(xform))
                continue;

            DrawEntityDebug(args, uid, zPhys);
        }
    }

    private void DrawEntityDebug(in OverlayDrawArgs args, EntityUid uid, CFZPhysicsComponent component)
    {
        var worldPos = _transform.GetWorldPosition(uid);
        var screenPos = args.ViewportControl?.WorldToScreen(worldPos) ?? Vector2.Zero;

        if (screenPos == Vector2.Zero)
            return;

        var localPos = float.Round(component.LocalPosition, 2);
        var groundDis = float.Round(component.LocalPosition - component.CachedGroundHeight, 2);
        var velocity = float.Round(component.Velocity, 2);

        var depthText = $"Z: {localPos}\n" +
                        $"G: {groundDis}\n" +
                        $"V: {velocity}\n" +
                        $"S: {component.CachedStickyGround}";

        args.ScreenHandle.DrawString(_font, screenPos, depthText, Color.White);
    }
}

public sealed partial class CFShowZLevelDebugCommand : LocalizedCommands
{
    [Dependency] private IOverlayManager _overlayManager = null!;

    public override string Command => "cf_showzleveldebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_overlayManager.HasOverlay<CFZLevelDebugOverlay>())
        {
            _overlayManager.RemoveOverlay<CFZLevelDebugOverlay>();
            return;
        }

        _overlayManager.AddOverlay(new CFZLevelDebugOverlay());
    }
}
