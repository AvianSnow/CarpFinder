using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Atmos.Components;
using Content.Server.CF.Adventure;
using Content.Shared.Atmos;
using Content.Shared.CF.Adventure;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Hazards;
using Content.Shared.CF.Shop;
using Content.Shared.Parallax;
using Content.Shared.Stacks;
using Content.Shared.Teleportation.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Adventure;

/// <summary>GM Core's budgets, and real generated sites.</summary>
[TestFixture]
[TestOf(typeof(CFAdventureSiteSystem))]
public sealed class CFAdventureSiteTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    private CFAdventureSiteSystem Sites => SEntMan.System<CFAdventureSiteSystem>();

    private CFAdventureSitePrototype Cave => Server.ProtoMan.Index(new ProtoId<CFAdventureSitePrototype>("Cave"));

    private int XpOf(EntProtoId id, int partyLevel)
    {
        var factory = SEntMan.ComponentFactory;
        Assert.That(Server.ProtoMan.Index(id).TryComp(out CFCombatantComponent combat, factory));
        CFExperienceAwards.TryAdversary(combat!.Level - partyLevel, out var xp);
        return xp;
    }

    private async Task<EntityCoordinates> Build(int level, int size, int seed)
    {
        Task<EntityCoordinates?> job = default!;
        await Server.WaitPost(() => job = Sites.Generate(Cave, level, size, seed));

        for (var i = 0; i < 600 && !job.IsCompleted; i++)
        {
            await Pair.RunTicksSync(5);
        }

        Assert.That(job.IsCompleted, "The site should finish generating.");
        var entrance = await job;
        Assert.That(entrance, Is.Not.Null);
        return entrance!.Value;
    }

    [Test]
    public void BudgetsFollowTheTable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CFEncounterRules.Budget(CFThreat.Low, 4), Is.EqualTo(60));
            Assert.That(CFEncounterRules.Budget(CFThreat.Moderate, 4), Is.EqualTo(80));
            Assert.That(CFEncounterRules.Budget(CFThreat.Severe, 4), Is.EqualTo(120));
            Assert.That(CFEncounterRules.Budget(CFThreat.Extreme, 4), Is.EqualTo(160));
            Assert.That(CFEncounterRules.Budget(CFThreat.Moderate, 5), Is.EqualTo(100), "One more character adds 20.");
            Assert.That(CFEncounterRules.Budget(CFThreat.Severe, 3), Is.EqualTo(90), "One fewer takes 30.");
            Assert.That(CFEncounterRules.Budget(CFThreat.Moderate, 1), Is.EqualTo(20));
        });
    }

    [Test]
    public void TreasureFollowsTheTable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CFEncounterRules.TreasureCopper(1, CFThreat.Low), Is.EqualTo(1300));
            Assert.That(CFEncounterRules.TreasureCopper(1, CFThreat.Moderate), Is.EqualTo(1800));
            Assert.That(CFEncounterRules.TreasureCopper(5, CFThreat.Severe), Is.EqualTo(20000));
            Assert.That(CFEncounterRules.TreasureCopper(20, CFThreat.Extreme), Is.EqualTo(9800000));
            Assert.That(CFEncounterRules.TreasureCopper(0, CFThreat.Low), Is.EqualTo(1300), "Below level 1 uses level 1.");
        });
    }

    [Test]
    public async Task EncountersNeverOverspendAndStayInLevelRange()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var level in new[] { 1, 2, 3, 5 })
            {
                foreach (var threat in new[] { CFThreat.Low, CFThreat.Moderate, CFThreat.Severe, CFThreat.Extreme })
                {
                    for (var i = 0; i < 25; i++)
                    {
                        var plan = Sites.PlanEncounter(Cave.Creatures, threat, level, 4);
                        Assert.That(plan.Sum(id => XpOf(id, level)), Is.LessThanOrEqualTo(CFEncounterRules.Budget(threat, 4)));
                        Assert.That(plan, Is.Not.Empty);
                    }
                }
            }
        });
    }

    [Test]
    public async Task HighLevelPartiesAreNotGivenWorthlessCreatures()
    {
        await Server.WaitAssertion(() =>
            Assert.That(Sites.PlanEncounter(Cave.Creatures, CFThreat.Severe, 15, 4), Is.Empty));
    }

    [Test]
    public async Task GeneratedSiteHasEncountersTreasureAndHazards()
    {
        var entrance = await Build(level: 1, size: 4, seed: 1234);

        await Server.WaitAssertion(() =>
        {
            var xforms = SEntMan.System<SharedTransformSystem>();
            var map = xforms.GetMapId(entrance);

            var creatures = 0;
            var query = SEntMan.EntityQueryEnumerator<CFCombatantComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var combat, out var xform))
            {
                if (xform.MapID == map && combat.DiesAtZeroHp)
                    creatures++;
            }

            var gold = 0;
            var coins = SEntMan.EntityQueryEnumerator<CFCoinComponent, StackComponent, TransformComponent>();
            while (coins.MoveNext(out var coin, out var stack, out var xform))
            {
                if (xform.MapID == map)
                    gold += coin.Copper * stack.Count;
            }

            var hazards = 0;
            var traps = SEntMan.EntityQueryEnumerator<CFHazardComponent, TransformComponent>();
            while (traps.MoveNext(out _, out var xform))
            {
                if (xform.MapID == map)
                    hazards++;
            }

            Assert.That(creatures, Is.GreaterThanOrEqualTo(4), "Four encounters, one creature each at least.");
            Assert.That(gold, Is.EqualTo((13 + 18 + 18 + 26) * CFCoins.CopperPerGold));
            Assert.That(hazards, Is.EqualTo(3));
        });
    }

    [TestCase(1)]
    [TestCase(77)]
    [TestCase(4096)]
    [TestCase(-512)]
    [TestCase(31337)]
    public async Task AnySeedGivesAUsableSite(int seed)
    {
        var entrance = await Build(level: 2, size: 3, seed: seed);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedTransformSystem>().GetMap(entrance), Is.Not.Null));
    }

    [Test]
    public async Task GeneratedMapsAreBreathableSky()
    {
        var entrance = await Build(level: 1, size: 4, seed: 99);

        await Server.WaitAssertion(() =>
        {
            var map = SEntMan.System<SharedTransformSystem>().GetMap(entrance);
            Assert.That(map, Is.Not.Null);

            var atmosphere = SEntMan.GetComponent<MapAtmosphereComponent>(map!.Value);
            var air = atmosphere.Mixture;
            Assert.Multiple(() =>
            {
                Assert.That(atmosphere.Space, Is.False, "CF is sky, not space.");
                Assert.That(air.Immutable);
                Assert.That(air.Temperature, Is.EqualTo(288.15f).Within(0.001f));
                Assert.That(air.GetMoles(Gas.Oxygen), Is.EqualTo(21.824879f).Within(0.001f));
                Assert.That(air.GetMoles(Gas.Nitrogen), Is.EqualTo(82.10312f).Within(0.001f));
                Assert.That(SEntMan.GetComponent<ParallaxComponent>(map.Value).Parallax, Is.EqualTo("Sky"));
            });
        });
    }

    [Test]
    public async Task RiftOpensIntoATwoWayPortal()
    {
        var map = await CFDevMap.Load(Pair, new Box2i(-1, -1, 2, 2));
        Entity<CFAdventureRiftComponent> rift = default;
        EntityUid hero = default;
        Task<bool> job = default!;

        await Server.WaitPost(() =>
        {
            var uid = SEntMan.SpawnEntity("CFAdventureRift", map.Local(0, 0));
            rift = (uid, SEntMan.GetComponent<CFAdventureRiftComponent>(uid));
            hero = SEntMan.SpawnEntity("MobHuman", map.Local(1, 0));
            job = Sites.Open(rift, hero);
        });

        for (var i = 0; i < 600 && !job.IsCompleted; i++)
        {
            await Pair.RunTicksSync(5);
        }

        Assert.That(await job, "The rift should open.");
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(rift), "The sealed rift is replaced.");

            var portals = new List<EntityUid>();
            var query = SEntMan.EntityQueryEnumerator<PortalComponent, LinkedEntityComponent>();
            while (query.MoveNext(out var uid, out _, out var link))
            {
                if (link.LinkedEntities.Count > 0
                    && SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "CFAdventurePortal")
                {
                    portals.Add(uid);
                }
            }

            Assert.That(portals, Has.Count.EqualTo(2));
            var xforms = SEntMan.System<SharedTransformSystem>();
            Assert.That(xforms.GetMapId(portals[0]), Is.Not.EqualTo(xforms.GetMapId(portals[1])), "It leads to another map.");
        });
    }
}
