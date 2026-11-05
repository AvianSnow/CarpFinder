using System;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Farming;
using Content.Server.CF.Outdoors;
using Content.Server.Weather;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Farming;
using Content.Shared.CF.Outdoors;
using Content.Shared.CF.Time;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Outdoors;

/// <summary>Day and night, temperature, weather and farming, on the dev map made into an outdoor world.</summary>
[TestFixture]
[TestOf(typeof(CFOutdoorsWorldSystem))]
public sealed class CFOutdoorsTest : GameTest
{
    private const int Hp = 500;

    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFOutdoorsTestHuman
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore

- type: entity
  id: CFOutdoorsTestRoof
  components:
  - type: Transform
    anchored: true
  - type: BlockWeather
";

    private CFDevMap _map = default!;

    private CFOutdoorsSystem Sky => SEntMan.System<CFOutdoorsSystem>();
    private CFFarmingSystem Farming => SEntMan.System<CFFarmingSystem>();

    private EntityUid MapUid => SEntMan.GetComponent<TransformComponent>(_map.Grid).MapUid!.Value;

    /// <summary>Makes the map outdoors and lays seedbed under the tile at x, y, which is also open to the sky.</summary>
    private Entity<CFOutdoorsComponent> Outdoors(CFTemperatureBand band = CFTemperatureBand.Normal)
    {
        var map = MapUid;
        var outdoors = SEntMan.EnsureComponent<CFOutdoorsComponent>(map);
        outdoors.Temperature = band;
        return (map, outdoors);
    }

    private void Seedbed(int x, int y)
    {
        var tile = Server.ResolveDependency<ITileDefinitionManager>()["CFFloorDirtSeedbed"];
        var coordinates = _map.Local(x, y);
        var maps = SEntMan.System<SharedMapSystem>();
        var grid = SEntMan.GetComponent<MapGridComponent>(_map.Grid);
        maps.SetTile(_map.Grid, grid, coordinates, new Tile(tile.TileId));
    }

    private EntityUid Human(int x = 0, int y = 0)
    {
        return SEntMan.SpawnEntity("CFOutdoorsTestHuman", _map.Local(x, y));
    }

    private int Health(EntityUid uid) => SEntMan.GetComponent<CFCombatantComponent>(uid).CurrentHp;

    private Entity<CFPlantComponent> Plant(string seed = "CFSeedWheat", int x = 0, int y = 0)
    {
        var item = SEntMan.SpawnEntity(seed, _map.Local(2, 1));
        var planted = Farming.TryPlant((item, SEntMan.GetComponent<CFSeedComponent>(item)), _map.Local(x, y), null);
        Assert.That(planted, Is.Not.Null);
        return (planted!.Value, SEntMan.GetComponent<CFPlantComponent>(planted.Value));
    }

    [Test]
    public void ClockRunsFourteenHoursOfDayThenTenOfNight()
    {
        var clock = new CFTimeComponent();
        Assert.Multiple(() =>
        {
            Assert.That(clock.IsDay(TimeSpan.Zero));
            Assert.That(clock.IsDay(TimeSpan.FromHours(3.4)));
            Assert.That(clock.IsDay(TimeSpan.FromHours(3.6)), Is.False);
            Assert.That(clock.IsDay(TimeSpan.FromHours(5.9)), Is.False);
            Assert.That(clock.IsDay(TimeSpan.FromHours(6.1)), "The next day starts after six real hours.");
        });
    }

    [Test]
    public void TemperatureFollowsTheTable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CFEnvironmentRules.Exposure(CFTemperatureBand.Normal), Is.Null);
            Assert.That(CFEnvironmentRules.Exposure(CFTemperatureBand.MildCold), Is.Null);
            Assert.That(CFEnvironmentRules.Exposure(CFTemperatureBand.SevereCold)!.Value.WorldMinutes, Is.EqualTo(60f));
            Assert.That(CFEnvironmentRules.Exposure(CFTemperatureBand.ExtremeHeat)!.Value.WorldMinutes, Is.EqualTo(10f));
            Assert.That(CFEnvironmentRules.Exposure(CFTemperatureBand.IncredibleCold)!.Value.Dice, Is.EqualTo(4));

            Assert.That(CFEnvironmentRules.Felt(CFTemperatureBand.SevereCold, true, false, true), Is.EqualTo(CFTemperatureBand.MildCold));
            Assert.That(CFEnvironmentRules.Felt(CFTemperatureBand.ExtremeCold, true, false, false), Is.EqualTo(CFTemperatureBand.SevereCold));
            Assert.That(CFEnvironmentRules.Felt(CFTemperatureBand.IncredibleCold, true, false, false), Is.EqualTo(CFTemperatureBand.IncredibleCold));
            Assert.That(CFEnvironmentRules.Felt(CFTemperatureBand.SevereHeat, false, true, false), Is.EqualTo(CFTemperatureBand.MildHeat));
            Assert.That(CFEnvironmentRules.Felt(CFTemperatureBand.SevereHeat, false, true, true), Is.EqualTo(CFTemperatureBand.SevereHeat), "Armor cancels desert clothing.");
        });
    }

    [Test]
    public async Task MapGetsALightCycleMatchingTheClock()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            Outdoors();
            var cycle = SEntMan.GetComponent<LightCycleComponent>(MapUid);
            Assert.That(cycle.Duration, Is.EqualTo(TimeSpan.FromHours(6)));
            Assert.That(cycle.Offset, Is.EqualTo(TimeSpan.FromHours(1.25)));
            Assert.That(cycle.InitialOffset, Is.False);
        });
    }

    [Test]
    public async Task ColdHurtsTheExposedAndGearProtects()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var map = Outdoors(CFTemperatureBand.SevereCold);
            Seedbed(0, 0);
            Seedbed(1, 0);
            var bare = Human(0, 0);
            var dressed = Human(1, 0);
            var coat = SEntMan.SpawnEntity("CFClothingColdWeather", _map.Local(1, 0));
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(dressed, coat, "outerClothing", force: true));

            SEntMan.System<CFOutdoorsWorldSystem>().Expose(map);

            Assert.That(Health(bare), Is.InRange(Hp - 6, Hp - 1));
            Assert.That(Health(dressed), Is.EqualTo(Hp));
        });
    }

    [Test]
    public async Task NormalWeatherAndIndoorsAreSafe()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var map = Outdoors();
            Seedbed(0, 0);
            var mob = Human(0, 0);
            SEntMan.System<CFOutdoorsWorldSystem>().Expose(map);
            Assert.That(Health(mob), Is.EqualTo(Hp));

            map.Comp.Temperature = CFTemperatureBand.IncredibleHeat;
            Assert.That(Sky.IsExposed(mob));

            Seedbed(1, 0);
            var sheltered = Human(1, 0);
            SEntMan.SpawnEntity("CFOutdoorsTestRoof", _map.Local(1, 0));
            Assert.That(Sky.IsExposed(sheltered), Is.False);

            SEntMan.System<CFOutdoorsWorldSystem>().Expose(map);
            Assert.That(Health(sheltered), Is.EqualTo(Hp));
            Assert.That(Health(mob), Is.LessThan(Hp));
        });
    }

    [Test]
    public async Task SeedsNeedASeedbedAndOneCropPerTile()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            Outdoors();
            var item = SEntMan.SpawnEntity("CFSeedWheat", _map.Local(2, 1));
            var seed = new Entity<CFSeedComponent>(item, SEntMan.GetComponent<CFSeedComponent>(item));

            Assert.That(Farming.CanPlant(seed, _map.Local(0, 0), null), Is.False);

            Seedbed(0, 0);
            Assert.That(Farming.CanPlant(seed, _map.Local(0, 0), null));

            Plant();
            Assert.That(Farming.CanPlant(seed, _map.Local(0, 0), null), Is.False);
        });
    }

    [Test]
    public async Task CropsGrowOnSunAndWaterThenHarvest()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            Outdoors();
            Seedbed(0, 0);
            var plant = Plant();
            var gatherable = new Entity<CFPlantGatherableComponent>(plant, SEntMan.GetComponent<CFPlantGatherableComponent>(plant));

            Assert.That(Farming.Harvest(gatherable), Is.False, "It has not grown yet.");

            for (var i = 0; i < 9; i++)
            {
                Farming.Grow(plant);
            }

            Assert.That(plant.Comp.GrowthLevel, Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(Farming.Harvest(gatherable));
        });

        await Pair.RunTicksSync(2);
    }

    [Test]
    public async Task ThirstyPlantsStopAndWaterRestartsThem()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            Outdoors();
            Seedbed(0, 0);
            var plant = Plant();
            plant.Comp.Resource = 0f;

            Farming.Grow(plant);
            Assert.That(plant.Comp.GrowthLevel, Is.Zero);

            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(plant.Owner, plant.Comp.Solution, out var solution));
            solutions.TryAddReagent(solution!.Value, CFPlantComponent.Water, 5, out _);

            Farming.Grow(plant);
            Assert.That(plant.Comp.GrowthLevel, Is.GreaterThan(0f));
        });
    }

    [Test]
    public async Task RainWatersExposedCrops()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Entity<CFPlantComponent> plant = default;
        await Server.WaitPost(() =>
        {
            Outdoors();
            Seedbed(0, 0);
            plant = Plant();
            var mapId = SEntMan.GetComponent<MapComponent>(MapUid).MapId;
            Assert.That(SEntMan.System<WeatherSystem>().TryAddWeather(mapId, "CFWeatherRain", out _, TimeSpan.FromMinutes(5)));
        });

        await Pair.RunSeconds(31);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(plant), Is.False, "The plant should still be standing.");
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(plant.Owner, plant.Comp.Solution, out _, out var water));
            Assert.That(water!.GetTotalPrototypeQuantity(CFPlantComponent.Water).Float(), Is.GreaterThan(0f));
        });
    }

    [Test]
    public async Task HarvestedVegetablesAreFood()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var potato = SEntMan.SpawnEntity("CFFoodPotato", _map.Local(2, 1));
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(potato, "food", out _, out var food));
            Assert.That(food!.GetTotalPrototypeQuantity("Nutriment").Float(), Is.GreaterThan(0f));
            Assert.That(SEntMan.HasComponent<CFSeedComponent>(potato), "Potatoes are planted whole.");
        });
    }

    [Test]
    public async Task WeatherScheduleStartsWeather()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var map = Outdoors();
            map.Comp.Weather.Add(new CFWeatherEntry { Weather = "CFWeatherSnowLight" });
            map.Comp.NextWeather = TimeSpan.Zero;
        });

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var mapId = SEntMan.GetComponent<MapComponent>(MapUid).MapId;
            Assert.That(SEntMan.System<WeatherSystem>().TryAddWeather(mapId, "CFWeatherSnowLight", out var ent));
            Assert.That(ent, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<CFOutdoorsComponent>(MapUid).NextWeather, Is.GreaterThan(TimeSpan.Zero));
        });
    }
}
