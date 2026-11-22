using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CF.Adventure;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Adventure;

/// <summary>Wandering mobs appear only on the listed floors, away from players, up to a cap.</summary>
[TestFixture]
[TestOf(typeof(CFWildSpawnerSystem))]
public sealed class CFWildSpawnerTest : GameTest
{
    private const string Mob = "CFMobGiantRat";

    private static readonly Box2i Footprint = new(0, 0, 1, 1);

    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private CFWildSpawnerSystem Spawner => SEntMan.System<CFWildSpawnerSystem>();

    private CFDevMap _map = default!;

    /// <summary>A spawner that only uses the floor the arena is made of, so the test does not depend on grass.</summary>
    private Entity<CFWildSpawnerComponent> Make(string tile, int maxAlive = 10)
    {
        var uid = SEntMan.Spawn(null, MapCoordinates.Nullspace);
        var comp = SEntMan.AddComponent<CFWildSpawnerComponent>(uid);
        comp.Table = new EntSelector { Id = Mob };
        comp.Tiles = new HashSet<ProtoId<ContentTileDefinition>> { tile };
        comp.MaxAlive = maxAlive;
        return (uid, comp);
    }

    private string ArenaFloor()
    {
        var maps = SEntMan.System<SharedMapSystem>();
        var grid = SEntMan.GetComponent<MapGridComponent>(_map.Grid);
        var tile = maps.GetTileRef(_map.Grid, grid, _map.Local(0, 0)).Tile;
        return Server.ResolveDependency<ITileDefinitionManager>()[tile.TypeId].ID;
    }

    private string TileUnder(EntityUid mob)
    {
        var maps = SEntMan.System<SharedMapSystem>();
        var xform = SEntMan.GetComponent<TransformComponent>(mob);
        var grid = SEntMan.GetComponent<MapGridComponent>(xform.GridUid!.Value);
        var tile = maps.GetTileRef(xform.GridUid.Value, grid, xform.Coordinates).Tile;
        return Server.ResolveDependency<ITileDefinitionManager>()[tile.TypeId].ID;
    }

    [Test]
    public async Task ItSpawnsOnTheListedFloorOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var floor = ArenaFloor();
            var spawner = Make(floor);
            var spawned = Spawner.TrySpawn(spawner, new[] { _map.Grid }, new List<MapCoordinates>());

            Assert.That(spawned, Has.Count.EqualTo(1));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(spawned[0]).EntityPrototype!.ID, Is.EqualTo(Mob));
            Assert.That(TileUnder(spawned[0]), Is.EqualTo(floor));

            var elsewhere = Make("CFFloorGrassTallBlood");
            Assert.That(Spawner.TrySpawn(elsewhere, new[] { _map.Grid }, new List<MapCoordinates>()), Is.Empty,
                "The arena has no such floor, so nothing may spawn.");
        });
    }

    [Test]
    public async Task ItKeepsAwayFromPlayers()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var spawner = Make(ArenaFloor());
            var transform = SEntMan.System<SharedTransformSystem>();
            var player = transform.ToMapCoordinates(_map.Local(0, 0));

            // The whole arena is within 1,000 tiles of the player.
            spawner.Comp.MinPlayerDistance = 1000f;
            Assert.That(Spawner.TrySpawn(spawner, new[] { _map.Grid }, new List<MapCoordinates> { player }), Is.Empty);

            spawner.Comp.MinPlayerDistance = 5f;
            for (var i = 0; i < 5; i++)
            {
                var spawned = Spawner.TrySpawn(spawner, new[] { _map.Grid }, new List<MapCoordinates> { player });
                Assert.That(spawned, Has.Count.EqualTo(1));
                var where = transform.GetMapCoordinates(spawned[0]);
                Assert.That((where.Position - player.Position).Length(), Is.GreaterThanOrEqualTo(5f));
            }
        });
    }

    [Test]
    public async Task ItStopsAtTheCap()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var spawner = Make(ArenaFloor(), maxAlive: 2);
            var grids = new[] { _map.Grid };
            var none = new List<MapCoordinates>();

            Assert.That(Spawner.TrySpawn(spawner, grids, none), Has.Count.EqualTo(1));
            var second = Spawner.TrySpawn(spawner, grids, none);
            Assert.That(second, Has.Count.EqualTo(1));
            Assert.That(Spawner.TrySpawn(spawner, grids, none), Is.Empty, "Two are alive, which is the cap.");

            SEntMan.DeleteEntity(second[0]);
            Assert.That(Spawner.TrySpawn(spawner, grids, none), Has.Count.EqualTo(1), "One is gone, so there is room again.");
        });
    }

    [Test]
    public void DragonRunUsesItWithoutTheDragon()
    {
        var map = Server.ProtoMan.Index(new ProtoId<Content.Shared.Maps.GameMapPrototype>("CFDragonRunTest"));
        var station = map.Stations.Values.Single();
        Assert.That(station.StationComponentOverrides.ContainsKey("CFWildSpawner"));

        var table = Server.ProtoMan.Index(new ProtoId<Content.Shared.EntityTable.EntityTablePrototype>("CFWildMobsDragonRun"));
        var ids = SEntMan.System<Content.Shared.EntityTable.EntityTableSystem>().GetSpawns(table.Table).ToList();
        Assert.That(ids, Is.Not.Empty);

        var group = (GroupSelector) table.Table;
        Assert.That(group.Children.OfType<EntSelector>().Select(c => c.Id.Id), Does.Not.Contain("CFMobJuvenileHornedDragon"));
    }
}
