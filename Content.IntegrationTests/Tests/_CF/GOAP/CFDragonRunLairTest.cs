using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CF.GOAP.Encounters;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>On Dragon Run, the juvenile horned dragon appears beside the dragon bones and guards them.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPEncounterSystem))]
public sealed class CFDragonRunLairTest : GameTest
{
    private static readonly ResPath Map = new("/Maps/_CF/DragonRunTest.yml");

    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [Test]
    public async Task TheDragonSpawnsBesideItsBones()
    {
        await Server.WaitPost(() =>
        {
            var options = new DeserializationOptions { InitializeMaps = true };
            Assert.That(SEntMan.System<MapLoaderSystem>().TryLoadMap(Map, out _, out _, options));
        });

        await Server.WaitAssertion(() =>
        {
            var transform = SEntMan.System<SharedTransformSystem>();
            var bones = Find("CFBoneDragon");
            Assert.That(bones, Has.Count.EqualTo(1));
            var spawners = Find("CFEncounterSpawnerDragonLair");
            Assert.That(spawners, Has.Count.EqualTo(1), "The map should have one dragon lair spawner.");

            var spawner = spawners[0];
            var comp = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(spawner);
            Assert.That(comp.Encounter, Is.EqualTo("DragonLair"));
            SEntMan.System<CFGOAPEncounterSystem>().Spawn((spawner, comp));

            var dragons = Find("CFMobJuvenileHornedDragon");
            Assert.That(dragons, Has.Count.EqualTo(1));
            var distance = (transform.GetWorldPosition(dragons[0]) - transform.GetWorldPosition(bones[0])).Length();
            Assert.That(distance, Is.LessThan(5f), "The dragon should appear by its bones.");
        });
    }

    private System.Collections.Generic.List<EntityUid> Find(string id)
    {
        var found = new System.Collections.Generic.List<EntityUid>();
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == id)
                found.Add(uid);
        }

        return found;
    }
}
