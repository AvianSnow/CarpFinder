using System.IO;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Outdoors;

// The test fills in the biome's bookkeeping by hand, which only the biome system may do.
#pragma warning disable RA0002

/// <summary>
/// A planet map whose biome had spawned entities that were since deleted could not be saved: every deleted key wrote
/// as <c>invalid</c> and collided. <c>CFBiomeSaveSystem</c> drops them before the save and marks their tiles modified.
/// </summary>
[TestFixture]
public sealed class CFBiomeSaveTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task AMapWhoseBiomeLostEntitiesStillSaves()
    {
        var entities = Server.EntMan;
        var mapSystem = Server.System<SharedMapSystem>();
        var loader = Server.System<MapLoaderSystem>();
        var chunk = new Vector2i(0, 0);
        var tileA = new Vector2i(1, 2);
        var tileB = new Vector2i(3, 4);
        EntityUid map = default;
        BiomeComponent biome = default!;
        var saved = false;
        var yaml = string.Empty;

        await Server.WaitPost(() =>
        {
            map = mapSystem.CreateMap(out _);
            biome = entities.AddComponent<BiomeComponent>(map);

            var a = entities.SpawnAttachedTo(null, new EntityCoordinates(map, Vector2.Zero));
            var b = entities.SpawnAttachedTo(null, new EntityCoordinates(map, Vector2.Zero));
            biome.LoadedEntities[chunk] = new()
            {
                [a] = tileA,
                [b] = tileB,
            };
            entities.DeleteEntity(a);
            entities.DeleteEntity(b);
        });

        await Server.WaitAssertion(() =>
        {
            using var writer = new StringWriter();
            saved = loader.TrySaveMap(map, writer);
            yaml = writer.ToString();
        });

        Assert.That(saved, Is.True, "The map saves although two of the biome's entities are gone.");
        Assert.That(yaml, Does.Not.Contain("invalid"));
        Assert.That(biome.LoadedEntities[chunk], Is.Empty);
        Assert.That(biome.ModifiedTiles[chunk], Is.EquivalentTo(new[] { tileA, tileB }));

        await Server.WaitPost(() => entities.DeleteEntity(map));
    }
}
