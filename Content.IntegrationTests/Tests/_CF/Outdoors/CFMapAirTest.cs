using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Maps;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Outdoors;

// The test plants air on a tile and reads it back, which only the atmosphere system may do.
#pragma warning disable RA0002

/// <summary>
/// A map saved in the editor keeps the sky's unchanging air on tiles that have since been floored over, because air is
/// not worked out while a map is paused. A wall on such a tile stopped a Debug server at round start
/// (<c>ShuntTileVolume</c> asserts), so run this with <c>-c Debug</c> to see that.
/// </summary>
[TestFixture]
public sealed class CFMapAirTest : GameTest
{
    private static readonly ProtoId<GameMapPrototype> RoundMap = "CFDragonRunTest";

    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static GasMixture Sky()
    {
        var sky = new GasMixture(Atmospherics.CellVolume) { Temperature = 290f };
        sky.AdjustMoles(Gas.Oxygen, 21.8f);
        sky.AdjustMoles(Gas.Nitrogen, 82.1f);
        sky.MarkImmutable();
        return sky;
    }

    [Test]
    public async Task FlooredOverSkyGetsAirOfItsOwn()
    {
        var map = await CFDevMap.Load(Pair, new Box2i(-1, -1, 3, 2), avoidStructures: true);
        await Pair.RunTicksSync(30);

        var open = map.Origin;
        var walled = map.Origin + new Vector2i(1, 0);
        GridAtmosphereComponent air = default!;

        await Server.WaitPost(() =>
        {
            air = SEntMan.GetComponent<GridAtmosphereComponent>(map.Grid);
            var sky = Sky();
            air.Tiles[open].Air = sky;
            air.Tiles[walled].Air = sky;

            SEntMan.SpawnEntity("WallSolid", map.Local(1, 0));
            var atmos = SEntMan.System<AtmosphereSystem>();
            atmos.InvalidateTile(map.Grid, open);
            atmos.InvalidateTile(map.Grid, walled);
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(air.Tiles[walled].Air, Is.Null, "A wall leaves no air on its tile.");
                Assert.That(air.Tiles[open].Air, Is.Not.Null);
                Assert.That(air.Tiles[open].Air!.Immutable, Is.False, "Floor has air that can change.");
                Assert.That(air.Tiles[open].Air!.GetMoles(Gas.Oxygen), Is.GreaterThan(1f), "It kept the air it had.");
            });
        });
    }

    [Test]
    public async Task TheRoundMapHasNoSkyAirOnFloor()
    {
        var path = Server.ProtoMan.Index(RoundMap).MapPath;
        HashSet<Entity<MapGridComponent>> grids = default!;

        await Server.WaitPost(() =>
        {
            var loader = SEntMan.System<MapLoaderSystem>();
            var options = new DeserializationOptions { InitializeMaps = true };
            Assert.That(loader.TryLoadMap(path, out _, out grids, options), $"Failed to load {path}");
        });

        await Pair.RunTicksSync(120);

        await Server.WaitAssertion(() =>
        {
            var stale = new List<Vector2i>();
            var seen = 0;
            foreach (var grid in grids)
            {
                if (!SEntMan.TryGetComponent(grid, out GridAtmosphereComponent air))
                    continue;

                foreach (var (indices, tile) in air.Tiles)
                {
                    seen++;
                    if (tile.Air is { Immutable: true } && !tile.MapAtmosphere)
                        stale.Add(indices);
                }
            }

            Assert.That(seen, Is.GreaterThan(0), "The map has air.");
            Assert.That(stale, Is.Empty, $"Floor with the sky's air: {string.Join(" ", stale.Take(10))}");
        });
    }
}
