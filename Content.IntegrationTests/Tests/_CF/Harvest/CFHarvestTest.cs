using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Shop;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Harvest;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Tools.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Harvest;

/// <summary>
/// Harvesting monster parts: it needs Survival, the check sets what the parts are worth, and it butchers the body.
/// </summary>
[TestFixture]
[TestOf(typeof(CFHarvestSystem))]
public sealed class CFHarvestTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    // A level 2 monster is worth 5 gp of parts. Its list is fixed, so the split is known: 4 leather and 1 bone.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: CFMobBoar
  id: CFCreatureTestHarvestBoar
  components:
  - type: CFCombatant
    level: 2
  - type: CFHarvestable
    parts:
    - id: CFLeather1
      amount: 4
    - id: CFBone1
      amount: 1

- type: entity
  parent: MobHuman
  id: CFHarvestTestHunter
  components:
  - type: CFCombatant
    level: 1
    trainedSkills: [ Survival ]
  - type: CFAbilityScore

- type: entity
  parent: MobHuman
  id: CFHarvestTestTownsfolk
  components:
  - type: CFCombatant
    level: 1
  - type: CFAbilityScore
";

    [Test]
    public void ValuesFollowBattlezooAndTheDegree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CFHarvestRules.Value(-1), Is.EqualTo(150), "1.5 gp at level -1.");
            Assert.That(CFHarvestRules.Value(2), Is.EqualTo(500), "5 gp at level 2.");
            Assert.That(CFHarvestRules.Value(20), Is.EqualTo(875_000), "8,750 gp at level 20.");
            Assert.That(CFHarvestRules.Yield(2, CFDegree.CriticalFailure), Is.Zero, "Ruined.");
            Assert.That(CFHarvestRules.Yield(2, CFDegree.Failure), Is.EqualTo(250), "Half.");
            Assert.That(CFHarvestRules.Yield(2, CFDegree.Success), Is.EqualTo(500));
            Assert.That(CFHarvestRules.Yield(2, CFDegree.CriticalSuccess), Is.EqualTo(750), "Half again: 10 gp becomes 15.");
        });
    }

    [Test]
    public async Task OnlySomeoneTrainedInSurvivalCanHarvest()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var harvest = SEntMan.System<CFHarvestSystem>();
            var boar = SEntMan.SpawnEntity("CFCreatureTestHarvestBoar", map.Local(0, 0));
            var hunter = SEntMan.SpawnEntity("CFHarvestTestHunter", map.Local(1, 0));
            var townsfolk = SEntMan.SpawnEntity("CFHarvestTestTownsfolk", map.Local(1, 1));
            var knife = SEntMan.SpawnEntity("CFSurvivalKnife", map.Local(1, 0));
            var target = new Entity<CFHarvestableComponent>(boar, SEntMan.GetComponent<CFHarvestableComponent>(boar));

            Assert.Multiple(() =>
            {
                Assert.That(harvest.CanHarvest(hunter));
                Assert.That(harvest.CanHarvest(townsfolk), Is.False);
                Assert.That(harvest.TryStartHarvest(hunter, target, knife), Is.False, "The boar is still alive.");
            });

            SEntMan.System<MobStateSystem>().ChangeMobState(boar, MobState.Dead);
            Assert.Multiple(() =>
            {
                Assert.That(harvest.TryStartHarvest(townsfolk, target, knife), Is.False, "Untrained.");
                Assert.That(harvest.TryStartHarvest(hunter, target, knife));
            });
        });
    }

    [Test]
    public async Task TheDegreeSetsWhatThePartsAreWorth()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var harvest = SEntMan.System<CFHarvestSystem>();
            var merchant = SEntMan.System<CFMerchantSystem>();
            var hunter = SEntMan.SpawnEntity("CFHarvestTestHunter", map.Local(1, 0));

            int Harvest(CFDegree degree)
            {
                var boar = SEntMan.SpawnEntity("CFCreatureTestHarvestBoar", map.Local(0, 0));
                SEntMan.System<MobStateSystem>().ChangeMobState(boar, MobState.Dead);
                var target = new Entity<CFHarvestableComponent>(boar, SEntMan.GetComponent<CFHarvestableComponent>(boar));
                var parts = harvest.Harvest(hunter, target, degree);
                Assert.That(target.Comp.Harvested);
                return parts.Sum(merchant.Worth);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Harvest(CFDegree.CriticalFailure), Is.Zero);
                Assert.That(Harvest(CFDegree.Failure), Is.EqualTo(250));
                Assert.That(Harvest(CFDegree.Success), Is.EqualTo(500));
                Assert.That(Harvest(CFDegree.CriticalSuccess), Is.EqualTo(750));
            });
        });
    }

    [Test]
    public async Task HarvestingButchersTheBody()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        EntityUid boar = default;
        List<EntityUid> parts = default!;
        await Server.WaitAssertion(() =>
        {
            var hunter = SEntMan.SpawnEntity("CFHarvestTestHunter", map.Local(1, 0));
            boar = SEntMan.SpawnEntity("CFCreatureTestHarvestBoar", map.Local(0, 0));
            SEntMan.System<MobStateSystem>().ChangeMobState(boar, MobState.Dead);
            var target = new Entity<CFHarvestableComponent>(boar, SEntMan.GetComponent<CFHarvestableComponent>(boar));
            parts = SEntMan.System<CFHarvestSystem>().Harvest(hunter, target, CFDegree.Success);
        });

        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(boar), "Butchering leaves no body.");
            Assert.That(parts.All(part => !SEntMan.Deleted(part)), "The parts are left where it lay.");
            var ids = parts.Select(part => SEntMan.GetComponent<MetaDataComponent>(part).EntityPrototype?.ID).ToList();
            Assert.That(ids.Distinct(), Is.EquivalentTo(new[] { "CFMonsterParts", "CFLeather1", "CFBone1" }),
                "Generic monster parts, and the leather and bone it lists.");

            var organs = 0;
            var query = SEntMan.AllEntityQueryEnumerator<Content.Shared.Body.OrganComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapID == map.MapId && xform.ParentUid == xform.GridUid)
                    organs++;
            }

            Assert.That(organs, Is.Zero, "No guts on the floor.");
        });
    }

    [Test]
    public async Task APlayerCharacterIsNeverHarvested()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var harvest = SEntMan.System<CFHarvestSystem>();
            var hunter = SEntMan.SpawnEntity("CFHarvestTestHunter", map.Local(1, 0));
            var knife = SEntMan.SpawnEntity("CFSurvivalKnife", map.Local(1, 0));
            var adventurer = SEntMan.SpawnEntity("CFHarvestTestTownsfolk", map.Local(0, 0));
            SEntMan.EnsureComponent<CFPvpComponent>(adventurer);
            SEntMan.System<MobStateSystem>().ChangeMobState(adventurer, MobState.Dead);

            var target = new Entity<CFHarvestableComponent>(adventurer,
                SEntMan.EnsureComponent<CFHarvestableComponent>(adventurer));
            Assert.That(harvest.IsHarvestable(target), Is.False);
            Assert.That(harvest.TryStartHarvest(hunter, target, knife), Is.False);
        });
    }

    [Test]
    public async Task HarvestingTakesOverStockButchering()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var spider = SEntMan.SpawnEntity("CFMobHuntingSpider", map.Local(0, 0));
            var kobold = SEntMan.SpawnEntity("CFKoboldWarrior", map.Local(1, 0));
            var bat = SEntMan.SpawnEntity("MobBat", map.Local(0, 1));
            var monkey = SEntMan.SpawnEntity("MobMonkey", map.Local(1, 1));
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<CFHarvestableComponent>(spider));
                Assert.That(SEntMan.HasComponent<CFHarvestableComponent>(kobold), "Every monster can be harvested.");
                Assert.That(SEntMan.HasComponent<CFHarvestableComponent>(bat), "A stock mob butchered by knife.");
                Assert.That(SEntMan.HasComponent<CFHarvestableComponent>(monkey), "A stock mob butchered on a spike.");
                Assert.That(SEntMan.GetComponent<CFHarvestableComponent>(bat).Parts, Is.Not.Empty,
                    "Its butchering list became its parts.");
            });
        });

        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var query = SEntMan.AllEntityQueryEnumerator<CFHarvestableComponent>();
            var left = new List<string>();
            while (query.MoveNext(out var uid, out _))
            {
                if (SEntMan.HasComponent<ToolRefinableComponent>(uid) || SEntMan.HasComponent<ButcherableComponent>(uid))
                    left.Add(SEntMan.ToPrettyString(uid));
            }

            Assert.That(left, Is.Empty, "Nothing can be butchered without the Survival check.");
        });
    }
}
