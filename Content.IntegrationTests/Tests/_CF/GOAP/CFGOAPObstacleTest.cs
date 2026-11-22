using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Encounters;
using Content.Server.CF.GOAP.Steering;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.GOAP;
using Content.Shared.CF.Preferences;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>Advanced NPCs getting through doors and glass, with real movement and real Strikes.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPSystem))]
public sealed class CFGOAPObstacleTest : CFGOAPLoggedTest
{
    private const int Extreme = 60;

    private const int HeroHp = 1000;

    private const int HeroX = 4;

    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: [ MobHuman, CFGOAPBrain ]
  id: CFObstacleTestBoss
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Monster

- type: entity
  parent: MobHuman
  id: CFObstacleTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 1
    maxHp: 1000
    currentHp: 1000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: [ CFWoodenDoorWindowed, CFLockSimple ]
  id: CFObstacleTestLockedDoor

- type: cfGoapProfile
  id: CFObstacleTestImpatient
  smashPatience: 3
";

    private CFDevMap _map = default!;

    private CFGOAPSystem Goap => SEntMan.System<CFGOAPSystem>();

    private IConfigurationManager Config => Server.ResolveDependency<IConfigurationManager>();

    private Entity<CFGOAPComponent> Boss(int strength = Extreme, string profile = "Default")
    {
        var boss = SEntMan.SpawnEntity("CFObstacleTestBoss", _map.Local(0, 0));
        SEntMan.GetComponent<CFAbilityScoreComponent>(boss).AbilityModifiers[CFAbility.Strength] = strength;
        var goap = SEntMan.GetComponent<CFGOAPComponent>(boss);
        goap.Profile = profile;

        var sword = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
        Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(boss, sword, checkActionBlocker: false));
        Goap.Start((boss, goap));
        return (boss, goap);
    }

    /// <summary>A hero sealed in by walls, except for <paramref name="gate"/> on the side facing the boss.</summary>
    private (EntityUid Hero, EntityUid Gate) Room(string gate)
    {
        var hero = SEntMan.SpawnEntity("CFObstacleTestHero", _map.Local(HeroX, 0));
        var gateUid = SEntMan.SpawnEntity(gate, _map.Local(HeroX - 1, 0));
        for (var x = HeroX - 1; x <= HeroX + 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                var isGate = x == HeroX - 1 && y == 0;
                var isHero = x == HeroX && y == 0;
                if (!isGate && !isHero)
                    SEntMan.SpawnEntity("WallSolid", _map.Local(x, y));
            }
        }

        return (hero, gateUid);
    }

    private void Wake(EntityUid mob)
    {
        Config.SetCVar(CCVars.NPCEnabled, true);
        SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
        SEntMan.System<NPCSystem>().WakeNPC(mob);
    }

    private int Hp(EntityUid mob)
    {
        return SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;
    }

    private bool Gone(EntityUid uid)
    {
        return !SEntMan.EntityExists(uid) || SEntMan.IsQueuedForDeletion(uid);
    }

    [Test]
    public async Task ItOpensADoorOnTheWayToWhereItLastSawThem()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            (hero, _) = Room("CFWoodenDoor");
            SEntMan.SpawnEntity("CFInvestigationNode", _map.Local(HeroX, 0));
            var boss = Boss();
            boss.Comp.Lkp = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);
            Wake(boss);
        });

        await Pair.RunSeconds(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Hp(hero), Is.LessThan(HeroHp), "It opened the door and got to them.");
            Config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    /// <summary>
    /// A longsword's plain hit cannot beat a grille's Hardness, so that is a wall to this creature, and glass is only
    /// slow. Both go on the arena's one safe row: a tile touching lava gets no arrow whatever stands on it.
    /// </summary>
    [Test]
    public async Task WhatItCannotBreakIsAWall()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss(strength: 4);
            var steering = SEntMan.System<CFGOAPSteeringSystem>();
            var grid = SEntMan.GetComponent<MapGridComponent>(_map.Grid);
            var tile = SEntMan.System<SharedMapSystem>().CoordinatesToTile(_map.Grid, grid, _map.Local(2, 0));
            var goal = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(_map.Local(4, 0)).Position;

            var grille = SEntMan.SpawnEntity("Grille", _map.Local(2, 0));
            var field = new CFGOAPFlowField();
            steering.BuildField(boss, field, goal);
            Assert.That(field.Arrows.ContainsKey(tile), Is.False, "No arrow stands on the grille.");

            SEntMan.DeleteEntity(grille);
            SEntMan.SpawnEntity("Window", _map.Local(2, 0));
            field = new CFGOAPFlowField();
            steering.BuildField(boss, field, goal);
            Assert.That(field.Arrows.ContainsKey(tile), "Glass it can break is only slow.");
        });
    }

    /// <summary>Too weak to hurt it, it does not stand there swinging. The door is a wall to it.</summary>
    [Test]
    public async Task ItLeavesAloneWhatItCannotHurt()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var (_, door) = Room("CFObstacleTestLockedDoor");
            var boss = Boss(-Extreme);
            var maps = SEntMan.System<SharedMapSystem>();
            var grid = SEntMan.GetComponent<MapGridComponent>(_map.Grid);

            var field = new CFGOAPFlowField();
            var goal = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(_map.Local(HeroX, 0)).Position;
            SEntMan.System<CFGOAPSteeringSystem>().BuildField(boss, field, goal);

            Assert.That(field.Arrows.ContainsKey(maps.CoordinatesToTile(_map.Grid, grid, _map.Local(HeroX - 1, 0))), Is.False);
            Assert.That(SEntMan.EntityExists(door));
        });
    }

    [Test]
    public async Task ATableIsNoPlaceToStand()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var boss = Boss();
            SEntMan.SpawnEntity("TableCard", _map.Local(2, 0));
            var transform = SEntMan.System<SharedTransformSystem>();
            var steering = SEntMan.System<CFGOAPSteeringSystem>();

            Assert.That(steering.IsWalkable(boss, transform.ToMapCoordinates(_map.Local(2, 0))), Is.False);
            Assert.That(steering.IsWalkable(boss, transform.ToMapCoordinates(_map.Local(3, 0))));
        });
    }

    [Test]
    public async Task ItClimbsATableInTheWay()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            (hero, _) = Room("TableCard");
            Wake(Boss());
        });

        await Pair.RunSeconds(15);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Hp(hero), Is.LessThan(HeroHp), "It got over the table to them.");
            Config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    [Test]
    public async Task ItSeesThroughGlass()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            Room("CFWindowWooden");
            var boss = Boss();
            Goap.Think(boss);
            Assert.That((boss.Comp.Facts & (uint) CFGOAPFact.TargetVisible) != 0);
        });
    }

    [Test]
    public async Task ItBreaksGlassAndClimbsThroughTheFrame()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, window = default;
        await Server.WaitPost(() =>
        {
            (hero, window) = Room("CFWindowWooden");
            Wake(Boss());
        });

        // Broken glass can trip it, and getting up and climbing both take a while.
        await Pair.RunSeconds(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Gone(window));
            Assert.That(Hp(hero), Is.LessThan(HeroHp), "It got to the other side.");
            Config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    /// <summary>Stock ends a climb when any fixture touches a wall, and the flanking sensor reaches past the body.</summary>
    [Test]
    public async Task ACombatantCanClimbBesideAWall()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("CFObstacleTestHero", _map.Local(0, 0));
            var frame = SEntMan.SpawnEntity("CFWindowWoodenBroken", _map.Local(1, 0));
            SEntMan.SpawnEntity("WallSolid", _map.Local(2, 0));
            SEntMan.System<ClimbSystem>().Climb(hero, hero, frame);
        });

        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ClimbingComponent>(hero).IsClimbing);
        });
    }

    /// <summary>Stock's "no room to stand" check used to count the flanking sensor, so any table in reach blocked it.</summary>
    [Test]
    public async Task ACombatantStandsUpBesideAFrame()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("CFObstacleTestHero", _map.Local(0, 0));
            SEntMan.SpawnEntity("CFWindowWoodenBroken", _map.Local(1, 0));
            SEntMan.System<CFConditionSystem>().KnockProne(hero);
        });

        await Pair.RunSeconds(8);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<CFConditionSystem>().IsProne(hero), Is.False);
        });
    }

    [Test]
    public async Task ItSmashesALockedDoor()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, door = default;
        await Server.WaitPost(() =>
        {
            (hero, door) = Room("CFObstacleTestLockedDoor");
            Wake(Boss());
        });

        await Pair.RunSeconds(16);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Gone(door));
            Assert.That(Hp(hero), Is.LessThan(HeroHp));
            Config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    [Test]
    public async Task PatienceDependsOnThePersonality()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        Entity<CFGOAPComponent> impatient = default, patient = default;
        EntityUid door = default;
        await Server.WaitPost(() =>
        {
            (_, door) = Room("CFObstacleTestLockedDoor");

            // It can hurt the door, so it tries, but it has far too many hit points to get through in time.
            SEntMan.System<CFCombatBridgeSystem>().TryEnsureCombatTarget(door, out var combat, out _, out _);
            combat.MaxHp = 100000;
            combat.CurrentHp = 100000;

            impatient = Boss(5, "CFObstacleTestImpatient");
            Wake(impatient);
        });

        await Pair.RunSeconds(7);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Gone(door), Is.False);
            var steering = SEntMan.GetComponent<CFGOAPSteeringComponent>(impatient);
            Assert.That(steering.OwnField.GaveUp, Does.ContainKey(door), "It ran out of patience.");
            Assert.That(steering.Obstacle, Is.Null);
            Assert.That((impatient.Comp.Facts & (uint) CFGOAPFact.NoWay) != 0, "There is no other way in.");
            Assert.That(impatient.Comp.Goal?.Id, Is.EqualTo("Idle"), "So it stopped trying.");

            SEntMan.QueueDeleteEntity(impatient);
        });

        await Server.WaitPost(() =>
        {
            patient = Boss(5);
            Wake(patient);
        });

        await Pair.RunSeconds(7);
        await Server.WaitAssertion(() =>
        {
            var steering = SEntMan.GetComponent<CFGOAPSteeringComponent>(patient);
            Assert.That(steering.Obstacle, Is.EqualTo(door), "The default personality is still at it.");
            Assert.That(steering.Working);
            Config.SetCVar(CCVars.NPCEnabled, false);
        });
    }

    [Test]
    public async Task AGroupBreaksInToFlank()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid window = default;
        await Server.WaitPost(() =>
        {
            (_, window) = Room("CFWindowWooden");
            var uid = SEntMan.SpawnEntity("CFEncounterSpawner", _map.Local(1, 0));
            var spawner = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(uid);
            SEntMan.System<CFGOAPEncounterSystem>().Spawn((uid, spawner));
            foreach (var member in spawner.Members)
            {
                // This is about what they do, not their dice. A kobold alone takes a long time over glass.
                var combat = SEntMan.GetComponent<CFCombatantComponent>(member);
                combat.PrintedAttack = Extreme;
                combat.PrintedDamage = Extreme;
                Wake(member);
            }
        });

        await Pair.RunSeconds(18);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Gone(window), "Seeing the hero through it, they broke it instead of pressing against the glass.");
            Config.SetCVar(CCVars.NPCEnabled, false);
        });
    }
}
