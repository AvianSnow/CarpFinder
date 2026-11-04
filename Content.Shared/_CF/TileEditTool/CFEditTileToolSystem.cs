using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.TileEditTool;

public sealed partial class CFEditTileToolSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private TurfSystem _turf = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CFEditTileToolComponent, AfterInteractEvent>(OnTileClick);
        SubscribeLocalEvent<CFEditTileToolComponent, CFTileToolReplaceDoAfter>(OnDoAfterEnd);
    }

    private void OnTileClick(Entity<CFEditTileToolComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        var doAfterArgs =
            new DoAfterArgs(EntityManager,
                args.User,
                ent.Comp.Delay,
                new CFTileToolReplaceDoAfter(GetNetCoordinates(args.ClickLocation)),
                ent)
            {
                BreakOnDamage = true,
                BlockDuplicate = false,
                CancelDuplicate = false,
                BreakOnMove = true,
                BreakOnHandChange = true,
            };
        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnDoAfterEnd(Entity<CFEditTileToolComponent> ent, ref CFTileToolReplaceDoAfter args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var location = GetCoordinates(args.Coordinates);

        var map = _transform.GetMap(location);
        if (!TryComp<MapGridComponent>(map, out var gridComp))
            return;

        var tileRef = _map.GetTileRef(map.Value, gridComp, location);

        var tile = _turf.GetContentTileDefinition(tileRef);

        if (!ent.Comp.TileReplace.TryGetValue(tile, out var replaceTile))
            return;

        args.Handled = true;

        _map.SetTile((map.Value, gridComp), location, new Tile(_proto.Index(replaceTile).TileId));
        _audio.PlayPredicted(ent.Comp.Sound, location, args.User);
    }
}
