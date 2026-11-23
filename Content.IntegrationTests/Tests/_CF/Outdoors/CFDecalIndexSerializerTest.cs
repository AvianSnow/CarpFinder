using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Decals;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests.CF.Outdoors;

/// <summary>
/// A biome keeps the decals it has loaded in a dictionary keyed by <see cref="DecalIndex"/>. Upstream gives that
/// struct no serializer, so saving a planet map threw. <c>CFDecalIndexSerializer</c> writes it as <c>x,y:id</c>.
/// </summary>
[TestFixture]
public sealed class CFDecalIndexSerializerTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task ADecalIndexRoundTripsAsADictionaryKey()
    {
        var serialization = Server.ResolveDependency<ISerializationManager>();
        var decals = new Dictionary<Vector2i, Dictionary<DecalIndex, Vector2i>>
        {
            [new Vector2i(-8, 16)] = new()
            {
                [new DecalIndex(new Vector2i(-8, 16), 3)] = new Vector2i(-5, 20),
                [new DecalIndex(new Vector2i(-8, 16), 40)] = new Vector2i(-1, 17),
            },
        };

        DataNode written = default!;
        Dictionary<Vector2i, Dictionary<DecalIndex, Vector2i>> read = default!;
        await Server.WaitPost(() =>
        {
            written = serialization.WriteValue(decals);
            read = serialization.Read<Dictionary<Vector2i, Dictionary<DecalIndex, Vector2i>>>(written);
        });

        var chunk = (MappingDataNode) ((MappingDataNode) written).Get("-8,16");
        Assert.That(chunk.TryGet("-8,16:3", out var tile), Is.True, "The key is written as x,y:id.");
        Assert.That(((ValueDataNode) tile!).Value, Is.EqualTo("-5,20"));
        Assert.That(read, Is.EqualTo(decals));
    }
}
