using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.NPC;

/// <summary>Painting, fading and following the influence map.</summary>
[TestFixture]
[TestOf(typeof(CFInfluenceMapSystem))]
public sealed class CFInfluenceMapTest : GameTest
{
    private const CFInfluenceLayer Scent = CFInfluenceLayer.Scent;

    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    private CFDevMap _map = default!;

    private CFInfluenceMapSystem Influence => SEntMan.System<CFInfluenceMapSystem>();

    private Vector2i Tile(int x, int y = 0)
    {
        return SEntMan.System<SharedMapSystem>().CoordinatesToTile(
            _map.Grid,
            SEntMan.GetComponent<MapGridComponent>(_map.Grid),
            _map.Local(x, y));
    }

    [Test]
    public async Task MarksFadeAndFreeTheirBlock()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var grid = _map.Grid;
            Influence.EnsureMap(grid);
            var map = SEntMan.GetComponent<CFInfluenceMapComponent>(grid);

            Influence.Paint(grid, Scent, Tile(1), 1f, Vector2.UnitX, grid);
            Assert.That(Influence.Cell(grid, Scent, Tile(1)).Intensity, Is.EqualTo(1f));
            Assert.That(map.Chunks, Has.Count.EqualTo(1));

            Influence.Decay((grid, map), map.HalfLifeSeconds);
            Assert.That(Influence.Cell(grid, Scent, Tile(1)).Intensity, Is.EqualTo(0.5f).Within(0.001f));

            Influence.Decay((grid, map), map.HalfLifeSeconds * 10f);
            Assert.That(Influence.Cell(grid, Scent, Tile(1)).Intensity, Is.Zero);
            Assert.That(map.Chunks, Is.Empty);
        });
    }

    [Test]
    public async Task WallsTakeNoMark()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var grid = _map.Grid;
            Influence.EnsureMap(grid);
            SEntMan.SpawnAtPosition("WallSolid", _map.Local(2, 0));

            Influence.Paint(grid, Scent, Tile(2), 1f, Vector2.UnitX, grid);
            Assert.That(Influence.Cell(grid, Scent, Tile(2)).Intensity, Is.Zero);
        });
    }

    [Test]
    public async Task ATrackerFollowsTheTrailForward()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var grid = _map.Grid;
            Influence.EnsureMap(grid);
            var tracker = SEntMan.SpawnEntity("MobHuman", _map.Local(2, 0));
            var quarry = SEntMan.SpawnEntity("MobHuman", _map.Local(5, 0));
            var other = SEntMan.SpawnEntity("MobHuman", _map.Local(5, 1));

            Assert.That(Influence.TryFollow(tracker, Scent, null, out _), Is.False, "Nothing to follow yet.");

            var map = SEntMan.GetComponent<CFInfluenceMapComponent>(grid);
            Paint(map, 1, 10, 1f, quarry);
            Paint(map, 2, 11, 0.8f, quarry);
            Paint(map, 3, 12, 0.3f, quarry);

            Assert.That(Influence.TryFollow(tracker, Scent, quarry, out var direction));
            Assert.That(direction.X, Is.GreaterThan(0.5f), "It heads for the newest mark, not back along the stronger one.");

            Assert.That(Influence.TryFollow(tracker, Scent, other, out _), Is.False, "Someone else's trail is not this one.");
        });
    }

    private void Paint(CFInfluenceMapComponent map, int x, int seconds, float intensity, EntityUid emitter)
    {
        var tile = Tile(x);
        var chunkKey = (Scent, new Vector2i((int) MathF.Floor(tile.X / 8f), (int) MathF.Floor(tile.Y / 8f)));
        if (!map.Chunks.TryGetValue(chunkKey, out var chunk))
        {
            chunk = new CFInfluenceChunk();
            map.Chunks[chunkKey] = chunk;
        }

        var index = (tile.Y % 8 + 8) % 8 * 8 + (tile.X % 8 + 8) % 8;
        chunk.Cells[index] = new CFInfluenceCell
        {
            Intensity = intensity,
            LastVisit = TimeSpan.FromSeconds(seconds),
            Heading = Vector2.UnitX,
            Emitter = emitter,
        };
    }
}
