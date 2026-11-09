using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Encounters;
using Content.Server.CF.GOAP.Sensors;
using Content.Server.CF.GOAP.Steps;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Hazards;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Pointing;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>The kobold trapper, its traps, and the KoboldAmbush encounter.</summary>
[TestFixture]
[TestOf(typeof(CFTrapLayerSystem))]
public sealed class CFKoboldTrapperTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFKoboldTrapperTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 200
    speedFeet: 25
    printedReflex: -50
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private EntityUid Mob(string id, int x) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private int Hp(EntityUid uid) => SEntMan.GetComponent<CFCombatantComponent>(uid).CurrentHp;

    private Entity<CFTrapLayerComponent> Trapper(int x = 0)
    {
        var uid = Mob("CFMobKoboldTrapper", x);
        return (uid, SEntMan.GetComponent<CFTrapLayerComponent>(uid));
    }

    private void Step(EntityUid mob, EntityUid onto)
    {
        SEntMan.System<SharedTransformSystem>().SetCoordinates(mob, SEntMan.GetComponent<TransformComponent>(onto).Coordinates);
        SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
    }

    /// <summary>Lays a trap on the tile east of the trapper.</summary>
    private EntityUid Lay(Entity<CFTrapLayerComponent> trapper)
    {
        var traps = SEntMan.System<CFTrapLayerSystem>();
        Assert.That(traps.TryFindSpot(trapper, Vector2Right, 0.5f, out var spot), "The tile beside it is open floor.");
        Assert.That(traps.TryLay(trapper, spot));

        var query = SEntMan.AllEntityQueryEnumerator<CFHazardComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            return uid;
        }

        Assert.Fail("No trap was built.");
        return default;
    }

    private static readonly System.Numerics.Vector2 Vector2Right = new(1f, 0f);

    [Test]
    public async Task CarriesWhatTheBookSays()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var trapper = Trapper();
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryGetActiveItem(trapper.Owner, out var held));
            Assert.That(SEntMan.HasComponent<GunComponent>(held!.Value), "It starts with the crossbow in hand.");

            Assert.That(SEntMan.System<InventorySystem>().TryGetSlotEntity(trapper, "pocket1", out var pocket), "A kobold needs a pocket for this.");
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(pocket!.Value).EntityPrototype?.ID, Is.EqualTo("CFLightHammer"));
            Assert.That(trapper.Comp.Charges, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task TrapTakesTheBuildersFactionAndSparesItsAllies()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid trap = default;
        EntityUid warrior = default;

        await Server.WaitPost(() =>
        {
            var trapper = Trapper();
            trap = Lay(trapper);
            warrior = Mob("CFKoboldWarrior", 5);
            Step(warrior, trap);
        });
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var hazard = SEntMan.GetComponent<CFHazardComponent>(trap);
            Assert.That(hazard.Armed, "An ally walking over it must not set it off.");
            Assert.That(Hp(warrior), Is.EqualTo(SEntMan.GetComponent<CFCombatantComponent>(warrior).MaxHp));
            Assert.That(SEntMan.System<CFHazardSystem>().IsVictim(trap, warrior), Is.False);
        });
    }

    [Test]
    public async Task TrapSpringsOnAnAdventurerThenIsGone()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid trap = default;
        EntityUid hero = default;
        var speed = 0f;

        await Server.WaitPost(() =>
        {
            trap = Lay(Trapper());
            hero = Mob("CFKoboldTrapperTestHero", 5);
            speed = SEntMan.GetComponent<MovementSpeedModifierComponent>(hero).CurrentSprintSpeed;
            Step(hero, trap);
        });
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(trap), "The trap is destroyed once it goes off.");
            Assert.That(Hp(hero), Is.LessThan(200));

            // Its Reflex is so low that even a natural 20 fails, so it is always slowed.
            Assert.That(SEntMan.System<CFBonusSystem>().Total(hero, CFBonusStat.Speed), Is.EqualTo(-5));
            Assert.That(
                SEntMan.GetComponent<MovementSpeedModifierComponent>(hero).CurrentSprintSpeed,
                Is.EqualTo(speed * 20f / 25f).Within(0.01f),
                "-5 feet on a 25 foot Speed.");
        });
    }

    [Test]
    public async Task TheTrapperKeepsItsOwnBrainInTheAmbush()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var encounters = SEntMan.System<CFGOAPEncounterSystem>();
            var uid = SEntMan.SpawnEntity("CFEncounterSpawner", _map.Local(3, 0));
            var spawner = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(uid);
            spawner.Encounter = "KoboldAmbush";
            encounters.Spawn((uid, spawner));

            Assert.That(spawner.Members, Has.Count.EqualTo(4));

            var trapper = spawner.Members.Single(SEntMan.HasComponent<CFTrapLayerComponent>);
            var goap = SEntMan.GetComponent<CFGOAPComponent>(trapper);
            Assert.That(goap.Actions.Select(a => a.Id), Does.Not.Contain("TakeFlank"), "It stays back and does not flank.");
            Assert.That(goap.Goals.Select(g => g.Id), Does.Contain("LayTrap"));

            foreach (var warrior in spawner.Members.Where(m => m != trapper))
            {
                var brain = SEntMan.GetComponent<CFGOAPComponent>(warrior);
                Assert.That(brain.Actions.Select(a => a.Id), Does.Contain("TakeFlank"));
            }
        });
    }

    [Test]
    public async Task PointingAtAnAdventurerSendsTheWarriors()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var encounters = SEntMan.System<CFGOAPEncounterSystem>();
            var uid = SEntMan.SpawnEntity("CFEncounterSpawner", _map.Local(3, 0));
            var spawner = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(uid);
            spawner.Encounter = "KoboldAmbush";
            encounters.Spawn((uid, spawner));

            var trapper = spawner.Members.Single(SEntMan.HasComponent<CFTrapLayerComponent>);
            var hero = Mob("CFKoboldTrapperTestHero", 0);

            var ally = new AfterPointedAtEvent(spawner.Members[0]);
            SEntMan.EventBus.RaiseLocalEvent(trapper, ref ally);
            Assert.That(spawner.Target, Is.Null, "Pointing at a friend is not an order.");

            var order = new AfterPointedAtEvent(hero);
            SEntMan.EventBus.RaiseLocalEvent(trapper, ref order);

            Assert.That(spawner.Target, Is.EqualTo(hero));
            foreach (var warrior in spawner.Members.Where(m => m != trapper))
            {
                var brain = SEntMan.GetComponent<CFGOAPComponent>(warrior);
                Assert.That(brain.Target, Is.EqualTo(hero));
                Assert.That(brain.Lkp, Is.Not.Null, "It is told where to go, even if it cannot see them.");
            }
        });
    }

    [Test]
    public async Task CornerDropsTheCrossbowAndDrawsTheHammer()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var trapper = Trapper();
            var goap = SEntMan.GetComponent<CFGOAPComponent>(trapper);
            var step = SEntMan.System<CFGOAPDrawSidearmStepSystem>();
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryGetActiveItem(trapper.Owner, out var crossbow));

            var action = Server.ProtoMan.Index<CFGOAPActionPrototype>("DrawSidearm");
            Assert.That(step.Run((trapper, goap), action), "It finishes in one go with an action to spend.");

            Assert.That(hands.IsHolding(trapper.Owner, crossbow!.Value), Is.False, "It dropped the crossbow.");
            Assert.That(hands.TryGetActiveItem(trapper.Owner, out var held), "The hammer is in the hand it swings with.");
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(held!.Value).EntityPrototype?.ID, Is.EqualTo("CFLightHammer"));
            Assert.That(SEntMan.System<InventorySystem>().TryGetSlotEntity(trapper, "pocket1", out _), Is.False);
        });
    }

    private CFGOAPSystem Goap => SEntMan.System<CFGOAPSystem>();

    /// <summary>A trapper with a brain ready to think, and an adventurer this far away.</summary>
    private (Entity<CFGOAPComponent> Trapper, EntityUid Hero) Standoff(int tiles)
    {
        var uid = Mob("CFMobKoboldTrapper", 0);
        var goap = SEntMan.GetComponent<CFGOAPComponent>(uid);
        Goap.Start((uid, goap));
        return ((uid, goap), Mob("CFKoboldTrapperTestHero", tiles));
    }

    /// <summary>Spends its traps' timer, for a decision that is not about traps.</summary>
    private void NoTrapsForNow(EntityUid trapper)
    {
        SEntMan.GetComponent<CFTrapLayerComponent>(trapper).NextLay = SGameTiming.CurTime + TimeSpan.FromHours(1);
    }

    private static string[] Plan(Entity<CFGOAPComponent> brain)
    {
        return brain.Comp.Plan.Select(step => step.Id).ToArray();
    }

    [Test]
    public async Task AtRangeItBuildsATrapFirst()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (trapper, _) = Standoff(5);
            Goap.Think(trapper);
            Assert.That(trapper.Comp.Goal?.Id, Is.EqualTo("LayTrap"));
            Assert.That(Plan(trapper), Is.EqualTo(new[] { "ConstructTrap" }));
        });
    }

    [Test]
    public async Task WithATrapDownItStandsAndShoots()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (trapper, _) = Standoff(5);
            NoTrapsForNow(trapper);
            Goap.Think(trapper);
            Assert.That(trapper.Comp.Goal?.Id, Is.EqualTo("KillTarget"));
            Assert.That(Plan(trapper), Is.EqualTo(new[] { "Shoot" }));
        });
    }

    [Test]
    public async Task ItBacksOffFromAnEnemyThatComesClose()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (trapper, _) = Standoff(2);
            Goap.Think(trapper);
            Assert.That(trapper.Comp.Goal?.Id, Is.EqualTo("KeepDistance"));
            Assert.That(Plan(trapper), Is.EqualTo(new[] { "Flee" }));
        });
    }

    [Test]
    public async Task CorneredItGoesForTheHammer()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (trapper, _) = Standoff(1);
            trapper.Comp.CorneredUntil = SGameTiming.CurTime + TimeSpan.FromSeconds(2);
            Goap.Think(trapper);
            Assert.That(trapper.Comp.Goal?.Id, Is.EqualTo("DrawSidearm"));
            Assert.That(Plan(trapper), Is.EqualTo(new[] { "DrawSidearm" }));
        });
    }

    [Test]
    public async Task AnEnemyThatStaysInReachCountsAsCornering()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (trapper, _) = Standoff(1);
            var standoff = SEntMan.GetComponent<CFGOAPStandoffComponent>(trapper);

            Goap.Think(trapper);
            Assert.That(trapper.Comp.Goal?.Id, Is.EqualTo("KeepDistance"), "At first it tries to get away.");

            standoff.InReachSince = SGameTiming.CurTime - standoff.CaughtTime;
            Goap.Think(trapper);
            Assert.That(trapper.Comp.Goal?.Id, Is.EqualTo("DrawSidearm"));
        });
    }

    [Test]
    public async Task OutOfBoltsItGoesForTheHammer()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (trapper, _) = Standoff(5);
            NoTrapsForNow(trapper);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryGetActiveItem(trapper.Owner, out var crossbow));

            var containers = SEntMan.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainer(crossbow!.Value, "projectiles", out var slot));
            foreach (var bolt in slot!.ContainedEntities.ToArray())
            {
                SEntMan.DeleteEntity(bolt);
            }

            Goap.Think(trapper);
            Assert.That(trapper.Comp.Goal?.Id, Is.EqualTo("DrawSidearm"));
        });
    }

    [Test]
    public async Task WithTheHammerOutItStaysAndFights()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var (trapper, _) = Standoff(1);
            var action = Server.ProtoMan.Index<CFGOAPActionPrototype>("DrawSidearm");
            Assert.That(SEntMan.System<CFGOAPDrawSidearmStepSystem>().Run(trapper, action));

            Goap.Think(trapper);
            Assert.That(trapper.Comp.Goal?.Id, Is.EqualTo("KillTarget"));
            Assert.That(Plan(trapper), Is.EqualTo(new[] { "Strike" }));
        });
    }

    /// <summary>Runs NPCs for a while. Asserts go on the test thread after this, where a failure keeps its message.</summary>
    private async Task RunNpcs(int ticks)
    {
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() => config.SetCVar(CCVars.NPCEnabled, true));
        await Pair.RunTicksSync(ticks);
        await Server.WaitPost(() => config.SetCVar(CCVars.NPCEnabled, false));
    }

    private void Wake(params EntityUid[] mobs)
    {
        foreach (var mob in mobs)
        {
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
            SEntMan.System<NPCSystem>().WakeNPC(mob);
        }
    }

    [Test]
    public async Task LiveItLaysATrapThenShoots()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid trapper = default;
        EntityUid hero = default;
        var startCharges = 0;

        await Server.WaitPost(() =>
        {
            trapper = Mob("CFMobKoboldTrapper", 0);
            hero = Mob("CFKoboldTrapperTestHero", 5);
            startCharges = SEntMan.GetComponent<CFTrapLayerComponent>(trapper).Charges;
            Wake(trapper, hero);
        });

        await RunNpcs(900);

        var charges = 0;
        var hp = 0;
        var armed = 0;
        await Server.WaitPost(() =>
        {
            charges = SEntMan.GetComponent<CFTrapLayerComponent>(trapper).Charges;
            hp = Hp(hero);
            var query = SEntMan.AllEntityQueryEnumerator<CFHazardComponent>();
            while (query.MoveNext(out _, out var hazard))
            {
                if (hazard.Armed)
                    armed++;
            }
        });

        Assert.That(charges, Is.LessThan(startCharges), "It built at least one trap.");
        Assert.That(armed, Is.GreaterThan(0), "The trap is on the floor and the kobold did not set it off.");
        Assert.That(hp, Is.LessThan(200), "It shot the adventurer.");
    }

    [Test]
    public async Task LiveTheAmbushClosesInWhileTheTrapperHangsBack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFGOAPEncounterSpawnerComponent spawner = default!;
        EntityUid trapper = default;
        EntityUid hero = default;
        var warriors = new List<EntityUid>();

        await Server.WaitPost(() =>
        {
            var uid = SEntMan.SpawnEntity("CFEncounterSpawner", _map.Local(0, 0));
            spawner = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(uid);
            spawner.Encounter = "KoboldAmbush";
            SEntMan.System<CFGOAPEncounterSystem>().Spawn((uid, spawner));

            trapper = spawner.Members.Single(SEntMan.HasComponent<CFTrapLayerComponent>);
            warriors.AddRange(spawner.Members.Where(m => m != trapper));
            hero = Mob("CFKoboldTrapperTestHero", 5);
            Wake(spawner.Members.Append(hero).ToArray());
        });

        await RunNpcs(600);

        var nearest = float.MaxValue;
        var trapperDistance = 0f;
        var alive = 0;
        var spent = 0;
        await Server.WaitPost(() =>
        {
            var transform = SEntMan.System<SharedTransformSystem>();
            var at = transform.GetWorldPosition(hero);
            foreach (var warrior in warriors)
            {
                if (SEntMan.Deleted(warrior))
                    continue;

                alive++;
                nearest = MathF.Min(nearest, (transform.GetWorldPosition(warrior) - at).Length());
            }

            trapperDistance = (transform.GetWorldPosition(trapper) - at).Length();
            spent = 6 - SEntMan.GetComponent<CFTrapLayerComponent>(trapper).Charges;
        });

        Assert.That(alive, Is.EqualTo(3), "No trap hurt a warrior.");
        Assert.That(nearest, Is.LessThan(2f), "The warriors went in.");
        Assert.That(trapperDistance, Is.GreaterThan(nearest), "The trapper stayed further back than they did.");
        Assert.That(spent, Is.GreaterThan(0), "It built a trap.");
    }

    /// <summary>The goal order that makes the design work, on the planner alone.</summary>
    [Test]
    public async Task TheGoalsOutrankShootingInTheRightOrder()
    {
        await Server.WaitAssertion(() =>
        {
            float Priority(string id) => Server.ProtoMan.Index<CFGOAPGoalPrototype>(id).Priority;

            Assert.That(Priority("LayTrap"), Is.GreaterThan(Priority("KillTarget")));
            Assert.That(Priority("KeepDistance"), Is.GreaterThan(Priority("LayTrap")));
            Assert.That(Priority("DrawSidearm"), Is.GreaterThan(Priority("KeepDistance")));
            Assert.That(Priority("RunAway"), Is.GreaterThan(Priority("DrawSidearm")));
        });
    }
}
