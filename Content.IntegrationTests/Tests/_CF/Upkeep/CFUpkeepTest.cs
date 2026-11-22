using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Upkeep;
using Content.Server.Station.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Upkeep;

/// <summary>
/// The dungeon wizard's cleanup, and what happens to a player's body when they respawn or leave it.
/// </summary>
[TestFixture]
[TestOf(typeof(CFDungeonCleanupSystem))]
[TestOf(typeof(CFPlayerBodySystem))]
public sealed class CFUpkeepTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 3);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFUpkeepTestBody

- type: entity
  id: CFUpkeepTestStation
  components:
  - type: StationJobs
    availableJobs:
      CFAlpha: [ 0, 1 ]
";

    private EntityUid MapOf(CFDevMap map)
    {
        return SEntMan.GetComponent<TransformComponent>(map.Grid).MapUid!.Value;
    }

    [Test]
    public async Task TheWizardTakesLooseThingsAndKeepsTheRest()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var cleanup = SEntMan.System<CFDungeonCleanupSystem>();
            var container = SEntMan.System<SharedContainerSystem>();
            var mobState = SEntMan.System<MobStateSystem>();

            var sword = SEntMan.SpawnEntity("CFLongsword", map.Local(0, 0));
            var blood = SEntMan.SpawnEntity("PuddleBlood", map.Local(1, 0));
            var bag = SEntMan.SpawnEntity("ClothingBackpack", map.Local(0, 1));
            var stored = SEntMan.SpawnEntity("CFLongsword", map.Local(0, 1));
            var coin = SEntMan.SpawnEntity("CFCopperCoin1", map.Local(1, 1));
            var living = SEntMan.SpawnEntity("CFUpkeepTestBody", map.Local(2, 0));
            var dead = SEntMan.SpawnEntity("CFUpkeepTestBody", map.Local(2, 1));

            var storage = container.GetContainer(bag, "storagebase");
            Assert.That(container.Insert(stored, storage), "The sword goes in the bag.");
            mobState.ChangeMobState(dead, MobState.Dead);

            var maps = new HashSet<EntityUid> { MapOf(map) };
            Assert.That(cleanup.Clean(maps, TimeSpan.FromHours(1)), Is.Zero,
                "Everything here was made right after the map, so it counts as the mapper's.");

            Assert.That(cleanup.Clean(maps, TimeSpan.Zero), Is.EqualTo(3));
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.IsQueuedForDeletion(sword), "A loose weapon goes.");
                Assert.That(SEntMan.IsQueuedForDeletion(blood), "Blood goes.");
                Assert.That(SEntMan.IsQueuedForDeletion(dead), "A dead body with no player goes.");
                Assert.That(SEntMan.IsQueuedForDeletion(bag), Is.False, "Containers stay.");
                Assert.That(SEntMan.IsQueuedForDeletion(stored), Is.False, "What is in them stays.");
                Assert.That(SEntMan.IsQueuedForDeletion(coin), Is.False, "Coins stay.");
                Assert.That(SEntMan.IsQueuedForDeletion(living), Is.False, "The living stay.");
            });
        });
    }

    [Test]
    public async Task ARespawnedPlayersBodyDropsEverythingAndGivesItsSlotBack()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid station = default;
        await Server.WaitAssertion(() =>
        {
            var bodies = SEntMan.System<CFPlayerBodySystem>();
            var inventory = SEntMan.System<InventorySystem>();
            var hands = SEntMan.System<SharedHandsSystem>();
            var container = SEntMan.System<SharedContainerSystem>();
            var stationJobs = SEntMan.System<ServerStationJobsSystem>();

            station = SEntMan.SpawnEntity("CFUpkeepTestStation", map.Local(0, 0));
            Assert.That(stationJobs.TrySetJobSlot(station, "CFAlpha", 0, createSlot: true));

            var body = SEntMan.SpawnEntity("CFUpkeepTestBody", map.Local(1, 1));
            var bag = SEntMan.SpawnEntity("ClothingBackpack", map.Local(1, 1));
            var sword = SEntMan.SpawnEntity("CFLongsword", map.Local(1, 1));
            var clothes = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.Local(1, 1));
            Assert.That(inventory.TryEquip(body, bag, "back", force: true));
            Assert.That(inventory.TryEquip(body, clothes, "jumpsuit", force: true));
            Assert.That(hands.TryPickupAnyHand(body, sword));

            var player = SEntMan.AddComponent<CFPlayerBodyComponent>(body);
            player.Station = station;
            player.Job = "CFAlpha";
            player.User = new NetUserId(System.Guid.NewGuid());

            // Respawning finds the body by its player, since a ghost's mind may have left it for good.
            Assert.That(bodies.ReleaseBodiesOf(player.User.Value), Is.EqualTo(1));

            Assert.Multiple(() =>
            {
                Assert.That(container.IsEntityInContainer(bag), Is.False, "The bag is on the floor.");
                Assert.That(container.IsEntityInContainer(sword), Is.False, "The weapon is on the floor.");
                Assert.That(container.IsEntityInContainer(clothes), Is.False, "So are its clothes.");
                Assert.That(SEntMan.IsQueuedForDeletion(body), "The body goes.");
            });
        });

        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<ServerStationJobsSystem>().TryGetJobSlot(station, "CFAlpha", out var slots));
            Assert.That(slots, Is.EqualTo(1), "The Alpha slot came back when the body was deleted.");
        });
    }

    [Test]
    public async Task ABodyLeftTooLongIsRemoved()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid body = default;
        EntityUid bag = default;

        await Server.WaitAssertion(() =>
        {
            body = SEntMan.SpawnEntity("CFUpkeepTestBody", map.Local(1, 1));
            bag = SEntMan.SpawnEntity("ClothingBackpack", map.Local(1, 1));
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(body, bag, "back", force: true));
            SEntMan.AddComponent<CFPlayerBodyComponent>(body).SsdLimit = TimeSpan.FromSeconds(1);
        });

        // Checks run every 5 seconds: one notices the body is empty, the next removes it.
        await Pair.RunTicksSync(12 * 30);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.Deleted(body), "No player came back for it.");
                Assert.That(SEntMan.Deleted(bag), Is.False, "Its bag was left on the floor.");
            });
        });
    }
}
