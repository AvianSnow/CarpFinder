using System;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Encounters;
using Content.Server.CF.GOAP.Society;
using Content.Server.CF.Temperature;
using Content.Server.NPC.Systems;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Combat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>
/// A tribe's life, with one kobold so nothing depends on who gets there first: it eats when hungry, waters a dry
/// plot, puts food it finds into the tribe's store, and asks a stranger for food instead of attacking.
/// No language model runs here, so this is the tribe's own rules.
/// </summary>
[TestFixture]
[TestOf(typeof(CFGOAPTribeSystem))]
public sealed class CFGOAPSocietyTest : CFGOAPLoggedTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSocietyTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 1000
    currentHp: 1000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFSocietyTestHero
  id: CFSocietyTestHeroCommon
  components:
  - type: CFLanguages
    known: [ Common ]

- type: entity
  parent: CFSocietyTestHero
  id: CFSocietyTestHeroDraconic
  components:
  - type: CFLanguages
    known: [ Draconic ]

# The kobold tribe with nothing laid down beside it: no mortar, knife, bucket or clippers.
- type: encounter
  id: CFSocietyTestTribeBare
  tribe: true
  mobs:
  - CFKoboldWarrior
  count: 1

- type: entity
  parent: BaseItem
  id: CFSocietyTestRubbish
  name: rubbish
  components:
  - type: Tag
    tags:
    - Trash
";

    private CFDevMap _map = default!;

    private IConfigurationManager Config => Server.ResolveDependency<IConfigurationManager>();

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private CFGOAPTribeSystem Tribes => SEntMan.System<CFGOAPTribeSystem>();

    private SatiationSystem Satiation => SEntMan.System<SatiationSystem>();

    private async Task Load()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitPost(() => Config.SetCVar(CCVars.NPCEnabled, true));
    }

    /// <summary>
    /// A tribe of one kobold warrior, fed and watered unless the test says otherwise. It comes with the kobold tribe's
    /// supplies (a mortar, a bucket, a knife and clippers) unless <paramref name="bare"/>.
    /// </summary>
    private EntityUid Tribe(out Entity<CFGOAPTribeComponent> tribe, float hunger = 1f, bool bare = false)
    {
        var spawner = SEntMan.SpawnEntity("CFEncounterSpawner", _map.Local(0, 0));
        var group = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(spawner);
        group.Encounter = bare ? "CFSocietyTestTribeBare" : "KoboldTribe";
        group.Mobs = "CFKoboldWarrior";
        group.Amount = 1;
        SEntMan.System<CFGOAPEncounterSystem>().Spawn((spawner, group));

        tribe = (spawner, SEntMan.GetComponent<CFGOAPTribeComponent>(spawner));

        // The test sets hunger itself.
        tribe.Comp.Appetite = 1f;
        var kobold = group.Members[0];
        Feed(kobold, hunger);
        SEntMan.System<SharedPhysicsSystem>().WakeBody(kobold);
        SEntMan.System<NPCSystem>().WakeNPC(kobold);
        return kobold;
    }

    private void Feed(EntityUid uid, float hunger)
    {
        var satiation = SEntMan.GetComponent<SatiationComponent>(uid);
        var most = Satiation.GetMaximumValue((uid, satiation), SatiationSystem.Hunger) ?? 100;
        Satiation.SetValue((uid, satiation), SatiationSystem.Hunger, most * hunger);
        var wet = Satiation.GetMaximumValue((uid, satiation), SatiationSystem.Thirst) ?? 100;
        Satiation.SetValue((uid, satiation), SatiationSystem.Thirst, (float) wet);
    }

    /// <summary>Runs the game half a second at a time until <paramref name="done"/>, for at most this many seconds.</summary>
    private async Task<bool> Until(float seconds, Func<bool> done)
    {
        var finished = false;
        for (var i = 0; i < seconds * 2 && !finished; i++)
        {
            await Pair.RunTicksSync(Timing.TickRate / 2);
            await Server.WaitPost(() => finished = done());
        }

        return finished;
    }

    [Test]
    public async Task AHungryMemberEatsTheFoodItFinds()
    {
        await Load();
        EntityUid kobold = default;
        EntityUid bread = default;
        await Server.WaitPost(() =>
        {
            bread = SEntMan.SpawnEntity("FoodBreadPlainSlice", _map.Local(3, 0));
            kobold = Tribe(out _, hunger: 0.1f);
        });

        var start = 0f;
        await Server.WaitPost(() => start = Tribes.Share(kobold, SatiationSystem.Hunger) ?? 1f);
        var ate = await Until(40, () => SEntMan.Deleted(bread) || (Tribes.Share(kobold, SatiationSystem.Hunger) ?? 0f) > start + 0.05f);
        Assert.That(ate, "A hungry kobold with a slice of bread three tiles away should have picked it up and eaten it.");
    }

    [Test]
    public async Task FoodItDoesNotNeedGoesIntoTheStore()
    {
        await Load();
        EntityUid bread = default;
        EntityUid crate = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            crate = SEntMan.SpawnEntity("CFSmallWoodenCrate", _map.Local(5, 1));
            bread = SEntMan.SpawnEntity("FoodBreadPlainSlice", _map.Local(2, 0));
            Tribe(out tribe);
            Assert.That(tribe.Comp.Stores, Does.Contain(crate), "The tribe should know the crate beside its nest as a store.");
        });

        var stored = await Until(40, () => SEntMan.GetComponent<StorageComponent>(crate).Container.Contains(bread));
        Assert.That(stored, "A fed kobold should carry loose food to the tribe's crate and put it in.");
        await Server.WaitAssertion(() => Assert.That(tribe.Comp.Stock[crate].Food, Is.EqualTo(1), "The tribe counts what it put in, without looking again."));
    }

    [Test]
    public async Task WheatBecomesBreadAtTheMortarTheWellAndTheFire()
    {
        await Load();
        EntityUid kobold = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity("Bonfire", _map.Local(4, 1));
            SEntMan.SpawnEntity("CFStoneWell", _map.Local(6, 1));
            SEntMan.SpawnEntity("WheatBushel", _map.Local(1, 0));
            kobold = Tribe(out tribe, hunger: 0.2f);
            Assert.That(tribe.Comp.Mortars, Is.Not.Empty, "The mortar the tribe brought is the tribe's.");
            Assert.That(tribe.Comp.Knives, Is.Not.Empty, "So is the knife.");
        });

        var ground = await Until(40, () => Exists("CFFlour"));
        Assert.That(ground, "A hungry kobold with wheat and a mortar should grind flour.");

        var mixed = await Until(40, () => Exists("CFFoodDoughLarge") || Exists("CFFoodBread") || Exists("CFFoodBreadSlice"));
        Assert.That(mixed, "Flour should be mixed into dough at the well.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(tribe.Comp.Fires, Is.Not.Empty, "The bonfire is the tribe's fire.");
            Assert.That(Tribes.TryGetFire(tribe.Comp, out _), "And it burns.");
            var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID != "CFFoodDoughLarge")
                    continue;

                Assert.That(Tribes.Stage(uid), Is.EqualTo(CFFoodStage.Cook), "Dough is for cooking.");
                Assert.That(Tribes.CanEat(kobold, uid), "A kobold could eat dough raw.");
                Assert.That(SEntMan.System<CFGOAPSocietySensorSystem>().TryFindStation(kobold, tribe.Comp, SEntMan.System<SharedTransformSystem>().GetMapCoordinates(kobold), uid, out _), "The fire is where dough goes.");
            }
        });

        var baked = await Until(40, () => Exists("CFFoodBread") || Exists("CFFoodBreadSlice"));
        Assert.That(baked, "Dough held over the fire should become bread.");

        var sliced = await Until(40, () => Exists("CFFoodBreadSlice"));
        Assert.That(sliced, "With a knife about, the loaf should be sliced.");

        var satiation = SEntMan.GetComponent<SatiationComponent>(kobold);
        var most = Satiation.GetMaximumValue((kobold, satiation), SatiationSystem.Hunger) ?? 100;
        var fed = await Until(40, () => Satiation.GetValueOrNull((kobold, satiation), SatiationSystem.Hunger) > most * 0.25f);
        Assert.That(fed, "And then it should eat.");
    }

    [Test]
    public async Task WheatNobodyCanGrindIsLeftWhereItLies()
    {
        await Load();
        EntityUid wheat = default;
        EntityUid crate = default;
        await Server.WaitPost(() =>
        {
            crate = SEntMan.SpawnEntity("CFSmallWoodenCrate", _map.Local(5, 1));
            wheat = SEntMan.SpawnEntity("WheatBushel", _map.Local(2, 0));
            Tribe(out _, hunger: 0.2f, bare: true);
        });

        await Pair.RunTicksSync(Timing.TickRate * 15);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedContainerSystem>().IsEntityInContainer(wheat), Is.False,
                "With no mortar, wheat is not food to the tribe: nobody picks it up, so nobody carries it in and out of the crate.");
            Assert.That(SEntMan.GetComponent<StorageComponent>(crate).Container.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task RubbishIsThrownOutOfTheNest()
    {
        await Load();
        EntityUid rubbish = default;
        EntityUid spawner = default;
        await Server.WaitPost(() =>
        {
            rubbish = SEntMan.SpawnEntity("CFSocietyTestRubbish", _map.Local(3, 0));
            Tribe(out var tribe);
            spawner = tribe.Owner;
        });

        var xform = SEntMan.System<SharedTransformSystem>();
        var thrown = await Until(30, () => SEntMan.Deleted(rubbish)
                                           || (xform.GetMapCoordinates(rubbish).Position - xform.GetMapCoordinates(spawner).Position).Length() > 8f);
        Assert.That(thrown, "Rubbish in the nest should be picked up and thrown well away from it.");
    }

    [Test]
    public async Task APuddleIsWipedUp()
    {
        await Load();
        EntityUid puddle = default;
        await Server.WaitPost(() =>
        {
            puddle = SEntMan.SpawnEntity("Puddle", _map.Local(3, 0));
            Tribe(out _);
        });

        var wiped = await Until(30, () => SEntMan.Deleted(puddle));
        Assert.That(wiped, "A puddle in the nest should be wiped up.");
    }

    [Test]
    public async Task ACoinGoesIntoTheStoreAndTheTribeRemembersIt()
    {
        await Load();
        EntityUid coin = default;
        EntityUid crate = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            crate = SEntMan.SpawnEntity("CFSmallWoodenCrate", _map.Local(5, 1));
            coin = SEntMan.SpawnEntity("CFSilverCoin1", _map.Local(2, 0));
            Tribe(out tribe);
        });

        var stored = await Until(40, () => SEntMan.GetComponent<StorageComponent>(crate).Container.Contains(coin));
        Assert.That(stored, "A coin lying in the nest should be put in the crate.");
        await Server.WaitAssertion(() => Assert.That(tribe.Comp.Stock[crate].Valuables, Is.EqualTo(1)));
    }

    [Test]
    public async Task ADeadFireIsFedAndLit()
    {
        await Load();
        EntityUid fire = default;
        EntityUid planks = default;
        await Server.WaitPost(() =>
        {
            fire = SEntMan.SpawnEntity("CFBonfire", _map.Local(4, 1));
            planks = SEntMan.SpawnEntity("CFWoodenPlanks10", _map.Local(2, 0));
            Tribe(out var tribe, bare: true);
            Assert.That(tribe.Comp.Fires, Does.Contain(fire), "An unlit bonfire is still the tribe's fire.");
        });

        var fireplaces = SEntMan.System<CFFireplaceSystem>();
        var lit = await Until(30, () => fireplaces.IsBurning(fire));
        Assert.That(lit, "A kobold should carry a plank to the dead fire and light it.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<StackComponent>(planks).Count, Is.EqualTo(9), "One plank went in. The rest stays for later.");
        });
    }

    [Test]
    public async Task AshFromTheFireFeedsPoorSoil()
    {
        await Load();
        EntityUid plot = default;
        await Server.WaitPost(() =>
        {
            plot = SEntMan.SpawnEntity("FungalSoil", _map.Local(6, 0));
            SEntMan.System<PlantTraySystem>().AdjustNutrient(plot, -1000f);
            SEntMan.SpawnEntity("CFBonfire", _map.Local(4, 1));
            SEntMan.SpawnEntity("CFWoodenPlanks10", _map.Local(2, 0));
            Tribe(out _, bare: true);
        });

        var ash = await Until(60, () => Exists("CFAsh1"));
        Assert.That(ash, "A plank burnt in the fire should leave ash beside it.");

        var fed = await Until(60, () => SEntMan.GetComponent<PlantTrayComponent>(plot).NutritionLevel > 0f);
        Assert.That(fed, "The poor plot should be fed with that ash.");
    }

    [Test]
    public async Task AHungryTribeHuntsAPigAndEatsIt()
    {
        await Load();
        EntityUid kobold = default;
        EntityUid pig = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity("Bonfire", _map.Local(4, 1));
            SEntMan.SpawnEntity("CFSurvivalKnife", _map.Local(2, 0));
            pig = SEntMan.SpawnEntity("MobPig", _map.Local(6, 0));
            kobold = Tribe(out tribe, hunger: 0.2f, bare: true);
            Assert.That(tribe.Comp.Butchers, Has.Count.EqualTo(1), "The survival knife beside the nest is the tribe's.");
            Assert.That(Tribes.IsGame(pig), "A pig speaks nothing and can be butchered.");
        });

        var seen = await Until(15, () => Tribes.Stance(tribe.Comp, pig) != null);
        Assert.That(seen, "The kobold should have noticed the pig.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Tribes.Stance(tribe.Comp, pig), Is.EqualTo(CFTribeStance.Hostile), "A hungry tribe hunts, it does not ask a pig for food.");
            foreach (var memory in SEntMan.GetComponent<CFGOAPSocialComponent>(kobold).Memory)
            {
                Assert.That(memory.Text, Does.Not.Contain("Adventurer"), "An animal is not a stranger.");
            }
        });

        var killed = await Until(60, () => SEntMan.Deleted(pig) || SEntMan.System<MobStateSystem>().IsDead(pig));
        Assert.That(killed, "It should kill the pig.");
        await Server.WaitAssertion(() =>
        {
            var society = SEntMan.System<CFGOAPSocietySensorSystem>();
            var here = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(kobold);
            Assert.That(society.IsCarcass(pig), "A dead pig is a carcass.");
            Assert.That(society.TryFindButcherKnife(kobold, tribe.Comp, here, out _), "The knife is to hand.");
            Assert.That(society.TryFindLooseFood(kobold, tribe.Comp, here, out var food) && food == pig, "So the carcass is loose food.");
        });

        var butchered = await Until(40, () => SEntMan.Deleted(pig));
        Assert.That(butchered, "It should fetch the knife and butcher the carcass, which takes the body away.");

        var satiation = SEntMan.GetComponent<SatiationComponent>(kobold);
        var most = Satiation.GetMaximumValue((kobold, satiation), SatiationSystem.Hunger) ?? 100;
        var ate = await Until(60, () => Satiation.GetValueOrNull((kobold, satiation), SatiationSystem.Hunger) > most * 0.25f);
        Assert.That(ate, "Then it should cook the meat at the fire and eat it.");
    }

    [Test]
    public async Task AFedTribeLeavesTheAnimalsAlone()
    {
        await Load();
        EntityUid pig = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            // A full crate, or the tribe would count its stock low and want the pig for that.
            var crate = SEntMan.SpawnEntity("CFSmallWoodenCrate", _map.Local(2, 1));
            var storage = SEntMan.System<SharedStorageSystem>();
            for (var i = 0; i < 5; i++)
            {
                Assert.That(storage.Insert(crate, SEntMan.SpawnEntity("FoodBreadPlainSlice", _map.Local(2, 1)), out _));
            }

            pig = SEntMan.SpawnEntity("MobPig", _map.Local(5, 0));
            Tribe(out tribe, bare: true);
            Assert.That(tribe.Comp.FoodStock, Is.EqualTo(5));
        });

        var seen = await Until(15, () => Tribes.Stance(tribe.Comp, pig) != null);
        Assert.That(seen, "The kobold should have noticed the pig.");
        await Pair.RunTicksSync(Timing.TickRate * 10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Tribes.Stance(tribe.Comp, pig), Is.EqualTo(CFTribeStance.Peace), "A fed tribe lets a pig be.");
            Assert.That(SEntMan.System<MobStateSystem>().IsAlive(pig));
            Assert.That(SEntMan.GetComponent<CFGOAPComponent>(tribe.Comp.Leader!.Value).Goal?.Id, Is.Not.EqualTo("Parley"), "And never asks it for food.");
        });
    }

    [Test]
    public async Task AHungryMemberTakesFoodOutOfTheCrate()
    {
        await Load();
        EntityUid kobold = default;
        EntityUid crate = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            crate = SEntMan.SpawnEntity("CFSmallWoodenCrate", _map.Local(5, 1));
            var storage = SEntMan.System<SharedStorageSystem>();
            for (var i = 0; i < 3; i++)
            {
                Assert.That(storage.Insert(crate, SEntMan.SpawnEntity("FoodBreadPlainSlice", _map.Local(5, 1)), out _));
            }

            // A loaf too, with no knife about: it is eaten whole.
            Assert.That(storage.Insert(crate, SEntMan.SpawnEntity("CFFoodBread", _map.Local(5, 1)), out _));
            kobold = Tribe(out tribe, hunger: 0.2f, bare: true);
            Assert.That(tribe.Comp.Stock[crate].Food, Is.EqualTo(4), "Slices and a loaf are all food to a tribe with no knife.");
        });

        var satiation = SEntMan.GetComponent<SatiationComponent>(kobold);
        var most = Satiation.GetMaximumValue((kobold, satiation), SatiationSystem.Hunger) ?? 100;
        var fed = await Until(60, () => Satiation.GetValueOrNull((kobold, satiation), SatiationSystem.Hunger) > most * 0.25f);
        Assert.That(fed, "A hungry kobold should take food out of the crate and eat it.");
        await Server.WaitAssertion(() => Assert.That(tribe.Comp.Stock[crate].Food, Is.LessThan(4), "And the tribe knows a piece is gone."));
    }

    [Test]
    public async Task AHungryMemberLooksInAChestItThinksIsEmpty()
    {
        await Load();
        EntityUid kobold = default;
        EntityUid crate = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            crate = SEntMan.SpawnEntity("CFSmallWoodenCrate", _map.Local(5, 1));
            kobold = Tribe(out tribe, hunger: 0.2f, bare: true);
            Assert.That(tribe.Comp.Stock[crate].Food, Is.EqualTo(0));

            // Food appears in the crate behind the tribe's back, and the tribe believes the crate was checked long ago.
            SEntMan.System<SharedStorageSystem>().Insert(crate, SEntMan.SpawnEntity("FoodBreadPlainSlice", _map.Local(5, 1)), out _);
            tribe.Comp.Stock[crate].At = Timing.CurTime - TimeSpan.FromMinutes(10);
            tribe.Comp.NextSurvey = Timing.CurTime + TimeSpan.FromMinutes(10);
        });

        var satiation = SEntMan.GetComponent<SatiationComponent>(kobold);
        var most = Satiation.GetMaximumValue((kobold, satiation), SatiationSystem.Hunger) ?? 100;
        var fed = await Until(40, () => Satiation.GetValueOrNull((kobold, satiation), SatiationSystem.Hunger) > most * 0.25f);
        Assert.That(fed, "Hungry and knowing of no food, it should go and look in the chest, and find the slice.");
    }

    [Test]
    public async Task OpenWaterIsDrunkFromAndDrawnIntoTheBucket()
    {
        await Load();
        EntityUid kobold = default;
        EntityUid plot = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity("FloorWaterEntity", _map.Local(7, 1));
            plot = SEntMan.SpawnEntity("HydroponicsSoil", _map.Local(4, 0));
            SEntMan.System<PlantTraySystem>().AdjustWater(plot, -1000f);
            SEntMan.SpawnEntity("CFBucket", _map.Local(2, 0));
            kobold = Tribe(out tribe, bare: true);
            Assert.That(tribe.Comp.Waters, Has.Count.EqualTo(1), "A pool of open water is the tribe's water, as a well is.");

            var satiation = SEntMan.GetComponent<SatiationComponent>(kobold);
            var wet = Satiation.GetMaximumValue((kobold, satiation), SatiationSystem.Thirst) ?? 100;
            Satiation.SetValue((kobold, satiation), SatiationSystem.Thirst, wet * 0.2f);
        });

        var satiation = SEntMan.GetComponent<SatiationComponent>(kobold);
        var wet = Satiation.GetMaximumValue((kobold, satiation), SatiationSystem.Thirst) ?? 100;
        var drank = await Until(40, () => Satiation.GetValueOrNull((kobold, satiation), SatiationSystem.Thirst) > wet * 0.25f);
        Assert.That(drank, "A thirsty kobold should drink from the pool.");

        var watered = await Until(90, () => SEntMan.GetComponent<PlantTrayComponent>(plot).WaterLevel > 0f);
        Assert.That(watered, "And fill the bucket at the pool for the dry plot.");
    }

    private bool Exists(string prototype)
    {
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var meta))
        {
            if (meta.EntityPrototype?.ID == prototype)
                return true;
        }

        return false;
    }

    [Test]
    public async Task ADryPlotIsLookedAtAndThenWatered()
    {
        await Load();
        EntityUid plot = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            plot = SEntMan.SpawnEntity("HydroponicsSoil", _map.Local(4, 0));
            SEntMan.System<PlantTraySystem>().AdjustWater(plot, -1000f);
            SEntMan.SpawnEntity("CFStoneWell", _map.Local(6, 1));
            SEntMan.SpawnEntity("CFBucket", _map.Local(2, 0));
            Tribe(out tribe);
            Assert.That(tribe.Comp.Plots, Does.Contain(plot));
            Assert.That(tribe.Comp.Waters, Has.Count.EqualTo(1), "The well should count as the tribe's water.");
        });

        var looked = await Until(30, () => tribe.Comp.Soil.ContainsKey(plot));
        Assert.That(looked, "Nobody had looked at the plot, so the kobold should have gone to.");

        var watered = await Until(60, () => SEntMan.GetComponent<PlantTrayComponent>(plot).WaterLevel > 0f);
        Assert.That(watered, "It saw the plot was dry, so it should have fetched the bucket, filled it at the well and poured it.");
    }

    [Test]
    public async Task AThirstyMemberDrinksAtTheWell()
    {
        await Load();
        EntityUid kobold = default;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity("CFStoneWell", _map.Local(5, 1));
            kobold = Tribe(out _);
            var satiation = SEntMan.GetComponent<SatiationComponent>(kobold);
            Satiation.SetValue((kobold, satiation), SatiationSystem.Thirst, 5f);
        });

        var start = 0f;
        await Server.WaitPost(() => start = Tribes.Share(kobold, SatiationSystem.Thirst) ?? 1f);
        var drank = await Until(60, () => (Tribes.Share(kobold, SatiationSystem.Thirst) ?? 0f) > start + 0.1f);
        Assert.That(drank, "A thirsty kobold should have walked to the tribe's well and drunk from it.");
    }

    /// <summary>The whole tribe: the trapper leads as the most skilled, and everyone lives by the tribe's goals.</summary>
    [Test]
    public async Task TheMostSkilledLeadsAndEveryMemberLivesAsTheTribeDoes()
    {
        await Load();
        await Server.WaitAssertion(() =>
        {
            var spawner = SEntMan.SpawnEntity("CFEncounterSpawnerKoboldTribe", _map.Local(3, 0));
            var group = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(spawner);
            SEntMan.System<CFGOAPEncounterSystem>().Spawn((spawner, group));
            var tribe = SEntMan.GetComponent<CFGOAPTribeComponent>(spawner);

            Assert.That(group.Members, Has.Count.EqualTo(6));
            Assert.That(tribe.Leader, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(tribe.Leader!.Value).EntityPrototype?.ID, Is.EqualTo("CFMobKoboldTrapper"));
            Assert.That(SEntMan.GetComponent<CFGOAPSocialComponent>(tribe.Leader.Value).Role, Is.EqualTo("Chief"));

            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var member in group.Members)
            {
                var goap = SEntMan.GetComponent<CFGOAPComponent>(member);
                Assert.That(goap.NeverSleeps, "A tribe lives on with nobody watching.");
                Assert.That(goap.Goals, Does.Contain(new Robust.Shared.Prototypes.ProtoId<Content.Shared.CF.GOAP.CFGOAPGoalPrototype>("TendCrops")));
                Assert.That(goap.Actions, Does.Contain(new Robust.Shared.Prototypes.ProtoId<Content.Shared.CF.GOAP.CFGOAPActionPrototype>("Parley")));
                Assert.That(names.Add(SEntMan.GetComponent<CFGOAPSocialComponent>(member).Name), "Every member has a name of its own.");
            }
        });

        // A minute of the tribe's life throws nothing.
        await Pair.RunTicksSync(Timing.TickRate * 60);
    }

    /// <summary>
    /// The whole way to a language model and back, against a stand-in server that answers as llama.cpp's does. By its
    /// own rules a hungry tribe talks first. Here the model says to defend the nest, and the leader's answer wins.
    /// </summary>
    [Test]
    public async Task TheLeaderTakesTheModelsAnswerOverItsOwnRules()
    {
        await Load();

        using var listener = new System.Net.HttpListener();
        var port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        string asked = null;
        var serving = Task.Run(async () =>
        {
            // The first question may be an idle one from before the stranger came. Every one gets the same answer,
            // and only the one that names the stranger can act on it.
            while (true)
            {
            var context = await listener.GetContextAsync();
            string body;
            using (var reader = new System.IO.StreamReader(context.Request.InputStream))
            {
                body = await reader.ReadToEndAsync();
            }

            if (body.Contains("Adventurer_1"))
                asked = body;

            const string answer = "{\"goal\": \"DefendNest\", \"target\": \"Adventurer_1\", \"utterance\": \"Out of our nest!\"}";
            var reply = System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = answer } } } });
            var bytes = System.Text.Encoding.UTF8.GetBytes(reply);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
            }
        });

        EntityUid hero = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlmUrl, $"http://127.0.0.1:{port}/v1/chat/completions");
            Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlm, true);
            Tribe(out tribe, hunger: 0.2f);
            hero = SEntMan.SpawnEntity("CFSocietyTestHero", _map.Local(6, 0));
        });

        try
        {
            // The answer comes back on another thread, in real time, so the game is given real time to get it.
            var hostile = false;
            for (var i = 0; i < 200 && !hostile; i++)
            {
                await Pair.RunTicksSync(5);
                await Task.Delay(50);
                await Server.WaitPost(() => hostile = Tribes.Stance(tribe.Comp, hero) == CFTribeStance.Hostile);
            }

            Assert.That(asked, Is.Not.Null, "The leader should have asked about the stranger.");
            Assert.That(asked, Does.Contain("\"grammar\""), "The question carries the grammar that limits the answer.");
            Assert.That(asked, Does.Contain("Adventurer_1"), "The stranger is named, so the answer can point at them.");
            Assert.That(hostile, "The model told the leader to defend the nest, so the tribe should have stopped talking.");
        }
        finally
        {
            await Server.WaitPost(() => Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlm, false));
            listener.Stop();
        }
    }

    /// <summary>
    /// Against the real model, started with <c>./runllm.sh</c>. Only runs when asked for by name, because it needs
    /// that server and takes as long as the model does.
    /// </summary>
    [Test, Explicit]
    public async Task ARealModelAnswersTheLeader()
    {
        await Load();
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlm, true);
            Tribe(out tribe, hunger: 0.2f);
            SEntMan.SpawnEntity("CFSocietyTestHero", _map.Local(6, 0));
        });

        try
        {
            for (var i = 0; i < 600 && tribe.Comp.Answers < 2; i++)
            {
                await Pair.RunTicksSync(3);
                await Task.Delay(100);
            }

            await TestContext.Out.WriteLineAsync($"Questions {tribe.Comp.Questions}, answers {tribe.Comp.Answers}, last: {tribe.Comp.LastAnswer}");
            Assert.That(tribe.Comp.Answers, Is.GreaterThanOrEqualTo(2), "The model should have given answers the game could use.");
        }
        finally
        {
            await Server.WaitPost(() => Config.SetCVar(Content.Shared.CF.CFCVars.SocietyLlm, false));
        }
    }

    /// <summary>
    /// A stranger's words reach the tribe's memory, and so the question to the model, when the kobold knows the
    /// language. In a tongue it does not know, it only remembers being spoken to.
    /// </summary>
    [Test]
    public async Task AStrangersWordsAreRememberedInALanguageTheKoboldKnows()
    {
        await Load();
        EntityUid kobold = default;
        EntityUid draconic = default;
        EntityUid common = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            kobold = Tribe(out tribe);
            draconic = SEntMan.SpawnEntity("CFSocietyTestHeroDraconic", _map.Local(3, 0));
            common = SEntMan.SpawnEntity("CFSocietyTestHeroCommon", _map.Local(4, 0));
        });

        var chat = SEntMan.System<Content.Server.Chat.Systems.ChatSystem>();
        await Server.WaitPost(() =>
        {
            chat.TrySendInGameICMessage(draconic, "We bring bread for the tribe", Content.Shared.Chat.InGameICChatType.Speak,
                Content.Shared.Chat.ChatTransmitRange.Normal, ignoreActionBlocker: true);
            chat.TrySendInGameICMessage(common, "Stand aside, lizards", Content.Shared.Chat.InGameICChatType.Speak,
                Content.Shared.Chat.ChatTransmitRange.Normal, ignoreActionBlocker: true);
        });

        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.System<CFSocietyAdvisorSystem>().Describe(tribe, (kobold, SEntMan.GetComponent<CFGOAPSocialComponent>(kobold)), out var labels);
            var memory = string.Join(" | ", state.Memory);
            var draconicLabel = tribe.Comp.Strangers[draconic].Label;
            var commonLabel = tribe.Comp.Strangers[common].Label;
            Assert.That(memory, Does.Contain($"{draconicLabel} said to us: 'We bring bread for the tribe'"), memory);
            Assert.That(memory, Does.Contain($"{commonLabel} spoke to us in a tongue we do not know."), memory);
            Assert.That(memory, Does.Not.Contain("Stand aside"), "Common is not a language the kobold knows.");
            Assert.That(labels[draconicLabel], Is.EqualTo(draconic), "The question names the speaker under the label the memory uses.");
            Assert.That(state.Heard, Is.EqualTo($"{commonLabel} spoke to us in a tongue we do not know."), "The last thing said is what the model answers.");
            Assert.That(labels[commonLabel], Is.EqualTo(common));
        });
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint) probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Test]
    public async Task AStrangerIsAskedForFoodAndFoodBuysPeace()
    {
        await Load();
        EntityUid kobold = default;
        EntityUid hero = default;
        Entity<CFGOAPTribeComponent> tribe = default;
        await Server.WaitPost(() =>
        {
            // Hungry, so it talks first even to someone standing in its nest.
            kobold = Tribe(out tribe, hunger: 0.2f);
            hero = SEntMan.SpawnEntity("CFSocietyTestHero", _map.Local(6, 0));
        });

        var goap = SEntMan.GetComponent<CFGOAPComponent>(kobold);
        var met = await Until(10, () => Tribes.Stance(tribe.Comp, hero) != null);
        Assert.That(met, "The kobold should have seen the stranger.");

        await Pair.RunTicksSync(Timing.TickRate * 4);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Tribes.Stance(tribe.Comp, hero), Is.EqualTo(CFTribeStance.Parley));
            Assert.That(goap.Goal?.Id, Is.EqualTo("Parley"), "It should be standing off and talking, not fighting.");
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.EqualTo(1000), "Nobody is hurt while they talk.");
        });

        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var bread = SEntMan.SpawnEntity("FoodBreadPlainSlice", _map.Local(6, 0));
            Assert.That(hands.TryPickupAnyHand(hero, bread, checkActionBlocker: false));
            Assert.That(hands.TryDrop(hero, bread, checkActionBlocker: false));
            Assert.That(Tribes.Stance(tribe.Comp, hero), Is.EqualTo(CFTribeStance.Peace), "Food dropped in front of the tribe is tribute.");
        });

        await Pair.RunTicksSync(Timing.TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(goap.Target, Is.Not.EqualTo(hero), "Whoever paid is left alone.");
            Assert.That(goap.Goal?.Id, Is.Not.EqualTo("KillTarget"));
            Assert.That(goap.Goal?.Id, Is.Not.EqualTo("Parley"));
        });

        // Blood settles it, whatever was paid.
        await Server.WaitPost(() =>
        {
            var hit = new CFDamagedEvent(hero, 1);
            SEntMan.EventBus.RaiseLocalEvent(kobold, ref hit);
            Assert.That(Tribes.Stance(tribe.Comp, hero), Is.EqualTo(CFTribeStance.Hostile));
            Assert.That(Tribes.TrySetStance(tribe.Comp, hero, CFTribeStance.Parley), Is.False, "A leader cannot talk its way back from that.");
        });
    }
}
