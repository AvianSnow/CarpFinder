// Ported from CrystallPunk 14 (CP14RoofSystem).
using Content.Shared.CF.Roof;
using Content.Shared.Ghost.Components;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Client.CF.Roof;

/// <summary>
/// Hides every roof while the local player stands under one. Ghosts always see roofs unless
/// <c>cf_toggleroof</c> turns them off. This is view state for one client, so it is never saved.
/// </summary>
public sealed partial class CFRoofSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public bool DisabledByCommand;

    private bool _underRoof;

    private bool RoofVisible => !_underRoof && !DisabledByCommand;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var player = _player.LocalEntity;
        if (HasComp<GhostComponent>(player) || !TryComp(player, out TransformComponent? xform))
            return;

        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var underRoof = false;
        foreach (var anchored in _map.GetAnchoredEntities(grid, gridComp, xform.Coordinates))
        {
            if (!HasComp<CFRoofComponent>(anchored))
                continue;

            underRoof = true;
            break;
        }

        if (underRoof == _underRoof)
            return;

        _underRoof = underRoof;
        UpdateRoofVisibilityAll();
    }

    [SubscribeLocalEvent]
    private void OnRoofStartup(Entity<CFRoofComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprite.SetVisible((ent, sprite), RoofVisible);
    }

    public void UpdateRoofVisibilityAll()
    {
        var query = AllEntityQuery<CFRoofComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite))
        {
            _sprite.SetVisible((uid, sprite), RoofVisible);
        }
    }
}

internal sealed partial class CFToggleRoofCommand : LocalizedCommands
{
    [Dependency] private IEntitySystemManager _systems = default!;

    public override string Command => "cf_toggleroof";

    public override string Help => "Toggle roof visibility";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var roof = _systems.GetEntitySystem<CFRoofSystem>();
        roof.DisabledByCommand = !roof.DisabledByCommand;
        roof.UpdateRoofVisibilityAll();
    }
}
