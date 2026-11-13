using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Items;
using Content.Shared.Clothing.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Items;

/// <summary>Weapons go on the belt or back, shields on the back, and a holstered one counts half its Bulk.</summary>
[TestFixture]
[TestOf(typeof(CFEncumbranceSystem))]
public sealed class CFHolsterTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task EveryWeaponCanBeHolsteredAndEveryShieldWornOnTheBack()
    {
        var missing = new List<string>();
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            foreach (var proto in Server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract
                    || !proto.ID.StartsWith("CF")
                    || proto.ID.Contains("Test")
                    || !proto.TryComp<CFWeaponComponent>(out _, factory)
                    || proto.TryComp<MobStateComponent>(out _, factory))
                    continue;

                var wanted = proto.TryComp<CFShieldComponent>(out _, factory)
                    ? SlotFlags.BACK
                    : SlotFlags.BELT | SlotFlags.BACK;

                if (!proto.TryComp<ClothingComponent>(out var clothing, factory) || (clothing.Slots & wanted) != wanted)
                    missing.Add(proto.ID);
            }
        });

        Assert.That(missing, Is.Empty, "Run .agent/port-scripts/holsters.py after adding a weapon or shield.");
    }

    [Test]
    public async Task AHolsteredWeaponCountsHalfItsBulk()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        int held = 0, belt = 0, back = 0, shield = 0, dagger = 0;
        await Server.WaitAssertion(() =>
        {
            var encumbrance = SEntMan.System<CFEncumbranceSystem>();
            var inventory = SEntMan.System<InventorySystem>();
            var hands = SEntMan.System<SharedHandsSystem>();
            var mob = SEntMan.SpawnEntity("MobHuman", map.Local(0, 0));

            // A longsword is 1 Bulk, so 10 tenths.
            var sword = SEntMan.SpawnEntity("CFLongsword", map.Local(0, 0));
            Assert.That(hands.TryPickupAnyHand(mob, sword, checkActionBlocker: false));
            held = encumbrance.CarriedTenths(mob);

            Assert.That(inventory.TryEquip(mob, sword, "belt", force: true));
            belt = encumbrance.CarriedTenths(mob);

            Assert.That(inventory.TryUnequip(mob, "belt", force: true));
            Assert.That(inventory.TryEquip(mob, sword, "back", force: true));
            back = encumbrance.CarriedTenths(mob);
            SEntMan.DeleteEntity(sword);

            var wooden = SEntMan.SpawnEntity("CFWoodenShield", map.Local(0, 0));
            Assert.That(inventory.TryEquip(mob, wooden, "back", force: true));
            shield = encumbrance.CarriedTenths(mob) * 2 - encumbrance.ItemTenths(wooden);
            SEntMan.DeleteEntity(wooden);

            // Light: 1 tenth, which stays 1 rather than weighing nothing.
            var knife = SEntMan.SpawnEntity("CFDagger", map.Local(0, 0));
            Assert.That(inventory.TryEquip(mob, knife, "belt", force: true));
            dagger = encumbrance.CarriedTenths(mob);
        });

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.EqualTo(10), "In hand it counts in full.");
            Assert.That(belt, Is.EqualTo(5));
            Assert.That(back, Is.EqualTo(5));
            Assert.That(shield, Is.InRange(0, 1), "A shield on the back counts half too.");
            Assert.That(dagger, Is.EqualTo(1));
        });
    }
}
