using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.CF.Temperature;
using Content.Shared.Atmos.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._CF.Temperature;

public sealed class CFFireSpreadSystem : CFSharedFireSpreadSystem
{
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly TileSystem _tile = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private readonly HashSet<Entity<CFFireSpreadComponent>> _spreadEntities = new();
    private readonly EntProtoId _firePrototype = "CFFire";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FlammableComponent, CFIgnitionDoAfter>(OnFlammableIgnited);
    }

    private void OnFlammableIgnited(Entity<FlammableComponent> ent, ref CFIgnitionDoAfter args)
    {
        if (args.Cancelled || args.Handled)
            return;

        _flammable.AdjustFireStacks(ent, ent.Comp.FirestacksOnIgnite, ent.Comp, true);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateAutoIgnite();
        UpdateFireSpread();
    }

    private void UpdateAutoIgnite()
    {
        var query = EntityQueryEnumerator<CFAutoIgniteComponent, FlammableComponent>();
        while (query.MoveNext(out var uid, out var autoIgnite, out var flammable))
        {
            if (!autoIgnite.Initialized || !flammable.Initialized)
                continue;

            if (autoIgnite.IgniteTime == TimeSpan.Zero)
                autoIgnite.IgniteTime = _gameTiming.CurTime + autoIgnite.IgniteDelay;

            if (_gameTiming.CurTime < autoIgnite.IgniteTime)
                continue;

            _flammable.AdjustFireStacks(uid, autoIgnite.StartStack, flammable, true);
            RemCompDeferred<CFAutoIgniteComponent>(uid);
        }
    }

    private void UpdateFireSpread()
    {
        _spreadEntities.Clear();
        var query = EntityQueryEnumerator<CFActiveFireSpreadingComponent, CFFireSpreadComponent, FlammableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var spread, out var flammable, out var xform))
        {
            if (!flammable.OnFire || spread.NextSpreadTime >= _gameTiming.CurTime || xform.ParentUid != xform.GridUid)
                continue;

            spread.NextSpreadTime = _gameTiming.CurTime +
                                    TimeSpan.FromSeconds(_random.NextFloat(spread.SpreadCooldownMin, spread.SpreadCooldownMax));
            _spreadEntities.Add(new Entity<CFFireSpreadComponent>(uid, spread));
        }

        foreach (var spread in _spreadEntities)
        {
            IgniteEntities(spread);
            IgniteTiles(spread);
        }
    }

    private void IgniteEntities(Entity<CFFireSpreadComponent> spread)
    {
        var targets = _lookup.GetEntitiesInRange<FlammableComponent>(
            _transform.GetMapCoordinates(spread),
            spread.Comp.Radius,
            LookupFlags.Uncontained);
        foreach (var target in targets)
        {
            if (_random.Prob(spread.Comp.Prob))
                _flammable.Ignite(target, spread);
        }
    }

    private void IgniteTiles(Entity<CFFireSpreadComponent> spread)
    {
        var xform = Transform(spread);
        if (!TryComp<MapGridComponent>(xform.ParentUid, out var grid))
            return;

        var position = xform.Coordinates.Position;
        var tiles = _map.GetLocalTilesIntersecting(
            xform.ParentUid,
            grid,
            new Box2(
                position + new Vector2(-spread.Comp.Radius, -spread.Comp.Radius),
                position + new Vector2(spread.Comp.Radius, spread.Comp.Radius))).ToList();

        foreach (var tile in tiles)
        {
            if (!_random.Prob(spread.Comp.ProbTile))
                continue;

            var definition = _turf.GetContentTileDefinition(tile);
            if (definition.BurnedTile is not { } burnedTile)
                continue;

            Spawn(_firePrototype, _map.ToCenterCoordinates(tile, grid));
            _tile.ReplaceTile(tile, (ContentTileDefinition) _tileDef[burnedTile]);
        }
    }
}
