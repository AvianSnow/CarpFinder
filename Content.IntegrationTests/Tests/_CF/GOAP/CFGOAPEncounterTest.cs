using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Encounters;
using Content.Server.CF.GOAP.Steering;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.GOAP;
using Content.Shared.DeviceLinking;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>The encounter spawner and the SmallGroup encounter.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPEncounterSystem))]
public sealed class CFGOAPEncounterTest : CFGOAPLoggedTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFGOAPEncounterTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 40
    maxHp: 1000
    currentHp: 1000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: encounter
  id: CFGOAPEncounterTestPlain
  mobs:
  - CFMobBoar
";

    private CFDevMap _map = default!;

    private CFGOAPEncounterSystem Encounters => SEntMan.System<CFGOAPEncounterSystem>();
    private CFGOAPSystem Goap => SEntMan.System<CFGOAPSystem>();
    private CFGOAPSteeringSystem Steering => SEntMan.System<CFGOAPSteeringSystem>();

    private Entity<CFGOAPEncounterSpawnerComponent> Spawner(int x = 0)
    {
        var uid = SEntMan.SpawnEntity("CFEncounterSpawner", _map.Local(x, 0));
        return (uid, SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(uid));
    }

    private EntityUid Hero(int x)
    {
        return SEntMan.SpawnEntity("CFGOAPEncounterTestHero", _map.Local(x, 0));
    }

    private void Kill(EntityUid mob)
    {
        SEntMan.System<MobStateSystem>().ChangeMobState(mob, Content.Shared.Mobs.MobState.Dead);
    }

    private Entity<CFGOAPComponent> Brain(EntityUid mob)
    {
        return (mob, SEntMan.GetComponent<CFGOAPComponent>(mob));
    }

    [Test]
    public async Task ItSpawnsWhenAnAdventurerComesNearAndOnlyOnce()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Entity<CFGOAPEncounterSpawnerComponent> spawner = default;
        await Server.WaitPost(() =>
        {
            spawner = Spawner();
            spawner.Comp.TriggerFeet = 15f;
            var hero = Hero(7);
            Server.ResolveDependency<ISharedPlayerManager>().SetAttachedEntity(Pair.Player!, hero);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(spawner.Comp.Members, Is.Empty, "35 feet away is too far for a 15-foot trigger.");
            spawner.Comp.TriggerFeet = 60f;
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(spawner.Comp.Members, Has.Count.EqualTo(4));
            Assert.That(spawner.Comp.Armed, Is.False);
        });

        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(spawner.Comp.Total, Is.EqualTo(4), "It spawns once.");
        });
    }

    [Test]
    public async Task ADmCanChooseTheMobsAndTheCount()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var spawner = Spawner();
            spawner.Comp.Mobs = "CFMobBoar, CFNotARealMob, CFMobGiantRat";
            spawner.Comp.Count = 3;

            Encounters.Spawn(spawner);

            var names = spawner.Comp.Members.Select(m => SEntMan.GetComponent<MetaDataComponent>(m).EntityPrototype!.ID).ToList();
            Assert.That(names, Is.EqualTo(new[] { "CFMobBoar", "CFMobGiantRat", "CFMobBoar" }));
            Assert.That(SEntMan.HasComponent<CFGOAPComponent>(spawner.Comp.Members[0]), "Any mob gets the group's brain.");
        });
    }

    [Test]
    public async Task EveryKoboldHasABrainAndAnEncounterSetsHowItFights()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var plain = SEntMan.SpawnEntity("CFKoboldWarrior", _map.Local(3, 0));
            Assert.That(SEntMan.HasComponent<CFGOAPComponent>(plain), "Every CF monster thinks with GOAP.");
            Assert.That(SEntMan.GetComponent<HTNComponent>(plain).RootTask.Task.Id, Is.EqualTo("CFGOAPCompound"));

            var spawner = Spawner();
            Encounters.Spawn(spawner);
            var member = SEntMan.GetComponent<CFGOAPComponent>(spawner.Comp.Members[0]);
            Assert.That(member.Tactics.Id, Is.EqualTo("Skirmisher"), "The encounter's tactics replace the kobold's own.");
            Assert.That(member.Goals, Does.Contain(new ProtoId<CFGOAPGoalPrototype>("RunAway")));
        });
    }

    [Test]
    public async Task ALeaderKeepsItsOwnWayOfFighting()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var alone = SEntMan.GetComponent<CFGOAPComponent>(SEntMan.SpawnEntity("CFMobKoboldTrapper", _map.Local(6, 0)));
            var spawner = Spawner();
            spawner.Comp.Encounter = "KoboldAmbush";
            Encounters.Spawn(spawner);

            var leader = spawner.Comp.Members
                .Select(m => SEntMan.GetComponent<CFGOAPComponent>(m))
                .Single(goap => goap.KeepOwnLists);
            Assert.That(leader.Goals, Is.EqualTo(alone.Goals));
            Assert.That(leader.Actions, Is.EqualTo(alone.Actions));
        });
    }

    [Test]
    public async Task AnEncounterThatNamesNoTacticsLeavesTheMonstersOwn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var spawner = Spawner();
            spawner.Comp.Encounter = "CFGOAPEncounterTestPlain";
            Encounters.Spawn(spawner);
            var boar = SEntMan.GetComponent<CFGOAPComponent>(spawner.Comp.Members[0]);
            Assert.That(boar.Tactics.Id, Is.EqualTo("Beast"));
        });
    }

    [Test]
    public async Task HalfDownBreaksTheGroup()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var spawner = Spawner();
            Encounters.Spawn(spawner);
            var first = spawner.Comp.Members[0];
            var second = spawner.Comp.Members[1];

            Kill(first);
            Assert.That(spawner.Comp.Members, Has.Count.EqualTo(3));
            Assert.That(spawner.Comp.Routed, Is.False);

            Kill(second);
            Assert.That(spawner.Comp.Members, Has.Count.EqualTo(2));
            Assert.That(spawner.Comp.Routed, "Half are down.");

            Kill(spawner.Comp.Members[0]);
            Kill(spawner.Comp.Members[0]);
            Assert.That(spawner.Comp.Members, Is.Empty);
        });
    }

    [Test]
    public async Task ItChoosesToFlankThenRunsThenHides()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var spawner = Spawner(1);
            var hero = Hero(5);
            Encounters.Spawn(spawner);
            var brain = Brain(spawner.Comp.Members[0]);
            Goap.Start(brain);

            Goap.Think(brain);
            Assert.That(brain.Comp.Target, Is.EqualTo(hero));
            Assert.That(spawner.Comp.Target, Is.EqualTo(hero));
            Assert.That(brain.Comp.Plan.Select(p => p.Id), Is.EqualTo(new[] { "TakeFlank", "StrikeFlanked" }));

            spawner.Comp.Routed = true;
            Goap.Think(brain);
            Assert.That(brain.Comp.Goal?.Id, Is.EqualTo("RunAway"));

            SEntMan.SpawnAtPosition("WallSolid", _map.Local(4, 0));
            SEntMan.SpawnAtPosition("WallSolid", _map.Local(4, 1));
            SEntMan.SpawnAtPosition("WallSolid", _map.Local(4, -1));
            Goap.Think(brain);
            Assert.That(brain.Comp.Goal?.Id, Is.EqualTo("GoToGround"), "It cannot see them any more.");

            SEntMan.EnsureComponent<CFStealthComponent>(brain).Active = true;
            Goap.Think(brain);
            Assert.That(brain.Comp.Goal?.Id, Is.EqualTo("StayHidden"));
        });
    }

    [Test]
    public async Task OnlyTheFirstMemberBuildsTheArrows()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var spawner = Spawner(1);
            var hero = Hero(6);
            Encounters.Spawn(spawner);
            SEntMan.SpawnAtPosition("WallSolid", _map.Local(4, 0));

            var goal = SEntMan.System<SharedTransformSystem>().GetWorldPosition(hero);
            var follower = Brain(spawner.Comp.Members[1]);
            Goap.Start(follower);
            Steering.Navigate(follower, goal, hero);
            Assert.That(spawner.Comp.Field.Arrows, Is.Empty, "A follower only reads.");

            var navigator = Brain(spawner.Comp.Members[0]);
            Goap.Start(navigator);
            Steering.Navigate(navigator, goal, hero);
            Assert.That(spawner.Comp.Field.Arrows, Is.Not.Empty);
            Assert.That(SEntMan.GetComponent<CFGOAPSteeringComponent>(navigator).Field, Is.SameAs(spawner.Comp.Field));

            SEntMan.GetComponent<CFGOAPSteeringComponent>(follower).NextNavigate = TimeSpan.Zero;
            Steering.Navigate(follower, goal, hero);
            Assert.That(SEntMan.GetComponent<CFGOAPSteeringComponent>(follower).Field, Is.SameAs(spawner.Comp.Field), "The whole group reads one field.");
        });
    }

    [Test]
    public async Task ARealGroupRunsWhenHalfAreDown()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        Entity<CFGOAPEncounterSpawnerComponent> spawner = default;
        var config = Server.ResolveDependency<IConfigurationManager>();
        var transform = SEntMan.System<SharedTransformSystem>();

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            hero = Hero(4);
            spawner = Spawner(2);
            Encounters.Spawn(spawner);
            foreach (var member in spawner.Comp.Members)
            {
                SEntMan.System<SharedPhysicsSystem>().WakeBody(member);
                SEntMan.System<NPCSystem>().WakeNPC(member);
            }
        });

        await Pair.RunTicksSync(60);

        var before = 0f;
        await Server.WaitPost(() =>
        {
            Kill(spawner.Comp.Members[0]);
            Kill(spawner.Comp.Members[0]);
            before = (transform.GetWorldPosition(spawner.Comp.Members[0]) - transform.GetWorldPosition(hero)).Length();
        });

        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            Assert.That(spawner.Comp.Routed);
            Assert.That(spawner.Comp.Members, Has.Count.EqualTo(2), "The meteor arena has lava around it. They must not run into it.");
            foreach (var member in spawner.Comp.Members)
            {
                var goal = Brain(member).Comp.Goal?.Id;
                Assert.That(goal, Is.AnyOf("RunAway", "GoToGround", "StayHidden"));
            }

            var after = (transform.GetWorldPosition(spawner.Comp.Members[0]) - transform.GetWorldPosition(hero)).Length();
            Assert.That(after, Is.GreaterThan(before + 1f), "It ran from the adventurer.");
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    [Test]
    public async Task ARealGroupFlanksItsTarget()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        Entity<CFGOAPEncounterSpawnerComponent> spawner = default;
        var config = Server.ResolveDependency<IConfigurationManager>();

        // The path graph takes a few seconds to build after a map loads.
        await Pair.RunTicksSync(300);

        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, true);
            hero = Hero(4);
            spawner = Spawner(1);
            Encounters.Spawn(spawner);
            foreach (var member in spawner.Comp.Members)
            {
                SEntMan.System<SharedPhysicsSystem>().WakeBody(member);
                SEntMan.System<NPCSystem>().WakeNPC(member);
            }
        });

        var flanked = false;
        var usedFlanking = false;
        for (var i = 0; i < 40 && !flanked; i++)
        {
            await Pair.RunTicksSync(15);
            await Server.WaitPost(() =>
            {
                var flanking = SEntMan.System<CFFlankingSystem>();
                flanked = spawner.Comp.Members.Any(member => flanking.IsOffGuardTo(hero, member));
                usedFlanking |= spawner.Comp.Members.Any(member => Brain(member).Comp.Plan.Any(step => step.Id is "TakeFlank" or "StrikeFlanked"));
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(flanked, "They should get around the target until it is off-guard to them.");
            Assert.That(usedFlanking, "It has to be on purpose, not kobolds crowding in by luck.");
            config.SetCVar(CCVars.NPCEnabled, false);
        });
    }
}
