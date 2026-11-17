using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Cooking;
using Content.Server.CF.Temperature;
using Content.Shared.CF.Cooking;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Placeable;
using Content.Shared.Tag;
using Content.Shared.Tools.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Cooking;

/// <summary>
/// Food made on a fire: something set on a burning bonfire is heated, a pot cooks what is in it into the dish its
/// ingredients fit, the dish is served into a bowl, and a barrel brews.
/// </summary>
[TestFixture]
[TestOf(typeof(CFCookerSystem))]
[TestOf(typeof(CFFireHeaterSystem))]
[TestOf(typeof(CFFermenterSystem))]
public sealed class CFCookerTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 1);

    public override PoolSettings PoolSettings => new() { Connected = false };

    private static readonly ProtoId<CFCookingRecipePrototype> MeatSoup = "MeatSoup";
    private static readonly EntProtoId Cabbage = "CFFoodCabbage";
    private static readonly EntProtoId CabbageSlice = "CFFoodCabbageSlice";

    private CFDevMap _map = default!;

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private SharedSolutionContainerSystem Solutions => SEntMan.System<SharedSolutionContainerSystem>();

    private string Proto(EntityUid uid) => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? string.Empty;

    /// <summary>A burning bonfire with one thing set on it, after the placer has noticed it.</summary>
    private async Task<EntityUid> OnAFire(string item, Action<EntityUid> prepare = null)
    {
        EntityUid fire = default;
        EntityUid placed = default;
        await Server.WaitPost(() =>
        {
            fire = SEntMan.SpawnEntity("CFBonfire", _map.Local(0, 0));
            var fuel = SEntMan.SpawnEntity("CFWoodenPlanks1", _map.Local(1, 0));
            Assert.That(SEntMan.System<CFFireplaceSystem>().TryFeed((fire, SEntMan.GetComponent<CFFireplaceComponent>(fire)), fuel, null));

            placed = SEntMan.SpawnEntity(item, _map.Local(0, 0));
            prepare?.Invoke(placed);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(placed);
        });

        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<CFFireplaceSystem>().IsBurning(fire), "The bonfire should be alight.");
            Assert.That(SEntMan.GetComponent<ItemPlacerComponent>(fire).PlacedEntities, Does.Contain(placed));
        });
        return placed;
    }

    private int Count(string proto)
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var meta))
        {
            if (meta.EntityPrototype?.ID == proto)
                count++;
        }

        return count;
    }

    [Test]
    public async Task DoughSetOnAFireBakes()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var dough = await OnAFire("CFFoodDoughLarge");

        await Pair.RunTicksSync(Timing.TickRate * 60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(dough), "A minute on the fire should bake the dough.");
            Assert.That(Count("CFFoodBread"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task APotCooksSoupAndABowlTakesIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var pot = await OnAFire("CFCookingPot", uid =>
        {
            Assert.That(Solutions.TryGetSolution(uid, "cooker", out var soln, out _), "The pot should have its solution.");
            Assert.That(Solutions.TryAddReagent(soln!.Value, "Water", 30, out _));

            var meat = SEntMan.SpawnEntity("CFFoodMeatLamb", _map.Local(1, 0));
            var container = SEntMan.System<SharedContainerSystem>().GetContainer(uid, "storagebase");
            Assert.That(SEntMan.System<SharedContainerSystem>().Insert(meat, container), "Meat should go in the pot.");

            var cooker = SEntMan.GetComponent<CFFoodCookerComponent>(uid);
            Assert.That(SEntMan.System<CFCookerSystem>().GetRecipe((uid, cooker))?.ID, Is.EqualTo("MeatSoup"));
        });

        // A soup takes its recipe's cooking time on the heat.
        var time = Server.ProtoMan.Index(MeatSoup).CookingTime.TotalSeconds;
        await Pair.RunTicksSync((int) (Timing.TickRate * (time + 8)));

        EntityUid bowl = default;
        await Server.WaitAssertion(() =>
        {
            var holder = SEntMan.GetComponent<CFFoodHolderComponent>(pot);
            Assert.That(holder.FoodData?.CurrentRecipe?.Id, Is.EqualTo("MeatSoup"), "The pot should hold meat soup.");
            Assert.That(SEntMan.System<SharedContainerSystem>().GetContainer(pot, "storagebase").ContainedEntities, Is.Empty, "The meat is in the soup.");

            Assert.That(Solutions.TryGetSolution(pot, "cooker", out _, out var potSolution));
            Assert.That(potSolution!.Volume.Float(), Is.GreaterThan(30f), "The meat's own reagents should be in the soup, beside the water.");

            bowl = SEntMan.SpawnEntity("CFBowlWooden", _map.Local(1, 0));
            var served = SEntMan.System<CFCookerSystem>().TryTransferFood((bowl, SEntMan.GetComponent<CFFoodHolderComponent>(bowl)), (pot, holder));
            Assert.That(served, "A bowl should take soup from the pot.");

            Assert.That(Solutions.TryGetSolution(bowl, "food", out _, out var soup));
            Assert.That(soup!.Volume.Float(), Is.GreaterThan(0f), "The bowl should hold soup.");
            Assert.That(SEntMan.GetComponent<CFFoodHolderComponent>(bowl).FoodData?.CurrentRecipe?.Id, Is.EqualTo("MeatSoup"));
        });
    }

    [Test]
    public async Task ABarrelBrewsBeerFromWheatAndWater()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("CFBaseBarrel", _map.Local(0, 0));
            var barrel = new Entity<CFFermenterComponent>(uid, SEntMan.GetComponent<CFFermenterComponent>(uid));
            var brewing = SEntMan.System<CFFermenterSystem>();

            var wheat = SEntMan.SpawnEntity("CFWheat", _map.Local(1, 0));
            Assert.That(brewing.TryMash(barrel, wheat), Is.False, "A dry barrel mashes nothing.");

            Assert.That(Solutions.TryGetSolution(uid, "barrel", out var soln, out var solution), "The barrel should have its solution.");
            Assert.That(Solutions.TryAddReagent(soln!.Value, "Water", 30, out _));
            Assert.That(brewing.TryMash(barrel, wheat));
            Assert.That(solution!.GetTotalPrototypeQuantity("CFWort").Float(), Is.EqualTo(10f));
            Assert.That(solution.GetTotalPrototypeQuantity("Water").Float(), Is.EqualTo(20f));

            for (var i = 0; i < 10; i++)
            {
                brewing.Ferment(barrel);
            }

            Assert.That(solution.GetTotalPrototypeQuantity("CFWort").Float(), Is.EqualTo(0f));
            Assert.That(solution.GetTotalPrototypeQuantity("CFBeer").Float(), Is.EqualTo(10f), "The wort should be beer.");
        });
    }

    /// <summary>Whatever goes in a cooker comes out as something, and what recipes ask for exists.</summary>
    [Test]
    public async Task EveryFoodTypeHasAFallbackAndEveryTagAnIngredient()
    {
        await Server.WaitAssertion(() =>
        {
            var carried = new HashSet<string>();
            foreach (var proto in Server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.Abstract && proto.TryComp<TagComponent>(out var tags, SEntMan.ComponentFactory))
                {
                    foreach (var tag in tags.Tags)
                    {
                        carried.Add(tag);
                    }
                }
            }

            var fallback = new HashSet<CFFoodType>();
            Assert.Multiple(() =>
            {
                foreach (var recipe in Server.ProtoMan.EnumeratePrototypes<CFCookingRecipePrototype>())
                {
                    if (recipe.Requirements.Count == 1 && recipe.Requirements[0] is CFAlwaysMet)
                        fallback.Add(recipe.FoodType);

                    if (recipe.FoodData.SliceProto is { } slice)
                        Assert.That(Server.ProtoMan.HasIndex<EntityPrototype>(slice), $"{recipe.ID}: {slice} does not exist.");

                    foreach (var requirement in recipe.Requirements)
                    {
                        if (requirement is not CFTagRequired required)
                            continue;

                        var found = false;
                        foreach (var tag in required.Tags)
                        {
                            found |= carried.Contains(tag);
                        }

                        Assert.That(found, $"{recipe.ID} asks for a tag nothing in the game has.");
                    }
                }

                foreach (var type in Enum.GetValues<CFFoodType>())
                {
                    Assert.That(fallback, Does.Contain(type), $"{type} has no recipe that always fits.");
                }
            });
        });
    }

    [Test]
    public async Task AWholeVegetableIsCutIntoSlicesACookCanUse()
    {
        await Server.WaitAssertion(() =>
        {
            var cabbage = Server.ProtoMan.Index(Cabbage);
            Assert.That(cabbage.TryComp<ToolRefinableComponent>(out var refinable, SEntMan.ComponentFactory));
            Assert.That(refinable!.RefineResult[0].PrototypeId?.Id, Is.EqualTo("CFFoodCabbageSlice"));

            var slice = Server.ProtoMan.Index(CabbageSlice);
            Assert.That(slice.TryComp<TagComponent>(out var tags, SEntMan.ComponentFactory));
            Assert.That(tags!.Tags, Does.Contain(new ProtoId<TagPrototype>("CFSalad")));
        });
    }
}
