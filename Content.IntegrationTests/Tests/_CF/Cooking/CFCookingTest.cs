using Robust.Shared.Timing;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Cooking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Temperature.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Cooking;

/// <summary>
/// Bread by hand, as a player makes it: wheat ground at the mortar, flour mixed into the well, dough held over heat,
/// and the loaf sliced. Each result takes the last thing's place in the hand.
/// </summary>
[TestFixture]
[TestOf(typeof(CFMortarSystem))]
[TestOf(typeof(CFMixIntoSystem))]
public sealed class CFCookingTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 1);

    public override PoolSettings PoolSettings => new() { Connected = false };

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private SharedInteractionSystem Interaction => SEntMan.System<SharedInteractionSystem>();

    private string Held(EntityUid who)
    {
        foreach (var held in Hands.EnumerateHeld(who))
        {
            return SEntMan.GetComponent<MetaDataComponent>(held).EntityPrototype?.ID ?? string.Empty;
        }

        return string.Empty;
    }

    private EntityUid HeldEntity(EntityUid who)
    {
        foreach (var held in Hands.EnumerateHeld(who))
        {
            return held;
        }

        return default;
    }

    [Test]
    public async Task WheatToSlicedBread()
    {
        var map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default;
        EntityUid mortar = default;
        EntityUid well = default;
        EntityUid wheat = default;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("MobHuman", map.Local(0, 0));
            mortar = SEntMan.SpawnEntity("CFMortarAndPestle", map.Local(1, 0));
            well = SEntMan.SpawnEntity("CFStoneWell", map.Local(2, 0));
            wheat = SEntMan.SpawnEntity("WheatBushel", map.Local(0, 0));
            Assert.That(Hands.TryPickupAnyHand(hero, wheat, checkActionBlocker: false));
            Interaction.InteractUsing(hero, wheat, mortar, SEntMan.GetComponent<TransformComponent>(mortar).Coordinates, checkCanInteract: false, checkCanUse: false);
        });

        // Grinding is a do-after of two seconds.
        await Pair.RunTicksSync(Timing.TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(wheat), "The wheat should be ground away.");
            Assert.That(Held(hero), Is.EqualTo("CFFlour"), "The flour should be in the hand that held the wheat.");
        });

        EntityUid flour = default;
        await Server.WaitPost(() =>
        {
            flour = HeldEntity(hero);
            Interaction.InteractUsing(hero, flour, well, SEntMan.GetComponent<TransformComponent>(well).Coordinates, checkCanInteract: false, checkCanUse: false);
        });

        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(flour));
            Assert.That(Held(hero), Is.EqualTo("CFFoodDoughLarge"), "Flour on the well should be dough at once.");
        });

        EntityUid dough = default;
        await Server.WaitPost(() =>
        {
            dough = HeldEntity(hero);
            SEntMan.System<SharedTemperatureSystem>().SetCookedTemperature(dough, 420f);
        });

        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(dough));
            Assert.That(Held(hero), Is.EqualTo("CFFoodBread"), "Hot dough should be bread, still in hand.");
        });

        EntityUid bread = default;
        EntityUid knife = default;
        await Server.WaitPost(() =>
        {
            bread = HeldEntity(hero);
            Assert.That(Hands.TryDrop(hero, bread, checkActionBlocker: false));
            knife = SEntMan.SpawnEntity("KitchenKnife", map.Local(0, 0));
            Assert.That(Hands.TryPickupAnyHand(hero, knife, checkActionBlocker: false));
            Interaction.InteractUsing(hero, knife, bread, SEntMan.GetComponent<TransformComponent>(bread).Coordinates, checkCanInteract: false, checkCanUse: false);
        });

        await Pair.RunTicksSync(Timing.TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(bread), "The loaf should be sliced away.");
            var slices = 0;
            var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var meta))
            {
                if (meta.EntityPrototype?.ID == "CFFoodBreadSlice")
                    slices++;
            }

            Assert.That(slices, Is.EqualTo(4), "A loaf is four slices.");
        });
    }
}
