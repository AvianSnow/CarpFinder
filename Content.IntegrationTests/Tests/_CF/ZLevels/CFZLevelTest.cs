using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.CF.ZLevels;
using Content.Shared.GameTicking;
using Content.Server.Maps;
using Content.Shared.Atmos.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.ZLevels;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Stunnable;
using Content.Shared.Teleportation.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.ZLevels;

/// <summary>Stacked maps, stairs, ladders, holes and falling. Levels are small bare maps built here.</summary>
[TestFixture]
[TestOf(typeof(CFZLevelSystem))]
public sealed class CFZLevelTest : GameTest
{
    private const int Hp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFZTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore

- type: entity
  parent: CFSpellArea
  id: CFZTestBlast
  components:
  - type: CFAreaEffect
    shape: Burst
    sizeFeet: 10
    filter: All
  - type: CFDegreeEffects
    effects:
    - !type:CFDamageEffect
      flat: 5
      damageType: Fire
      scaleByDegree: false
";

    private readonly List<(MapId Map, Entity<MapGridComponent> Grid)> _levels = new();

    private CFZLevelSystem Levels => SEntMan.System<CFZLevelSystem>();

    /// <summary>A level with a square of floor, or none at all for open air.</summary>
    private MapId Level(bool floor = true)
    {
        var maps = SEntMan.System<SharedMapSystem>();
        maps.CreateMap(out var id);
        var grid = maps.CreateGridEntity(id);
        var plating = Server.ResolveDependency<ITileDefinitionManager>()["Plating"];
        for (var x = -4; x <= 4; x++)
        {
            for (var y = -4; y <= 4; y++)
            {
                // A level of open air keeps one far tile so the grid exists.
                var open = !floor && (x, y) != (4, 4);
                if (!open)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }
        }

        _levels.Add((id, grid));
        return id;
    }

    private EntityCoordinates At(MapId map, int x, int y)
    {
        var (_, grid) = _levels.Find(l => l.Map == map);
        return SEntMan.System<SharedMapSystem>().GridTileToLocal(grid.Owner, grid.Comp, new Vector2i(x, y));
    }

    private EntityUid Hero(MapId map, int x = 0, int y = 0)
    {
        return SEntMan.SpawnAtPosition("CFZTestHero", At(map, x, y));
    }

    private MapId MapOf(EntityUid uid) => SEntMan.GetComponent<TransformComponent>(uid).MapID;

    private int Health(EntityUid uid) => SEntMan.GetComponent<CFCombatantComponent>(uid).CurrentHp;

    [SetUp]
    public void Reset()
    {
        _levels.Clear();
    }

    [TestCase(5, 0)]
    [TestCase(10, 5)]
    [TestCase(20, 10)]
    [TestCase(1500, 750)]
    [TestCase(5000, 750)]
    public void FallDamageFollowsTheRule(int feet, int damage)
    {
        Assert.That(CFFallRules.Damage(feet), Is.EqualTo(damage));
    }

    [Test]
    public async Task LevelsKnowTheirNeighbours()
    {
        await Server.WaitAssertion(() =>
        {
            var low = Level();
            var high = Level();
            Levels.Combine(new[] { low, high });

            Assert.That(Levels.MapAtOffset(low, 1), Is.EqualTo(high));
            Assert.That(Levels.MapAtOffset(high, -1), Is.EqualTo(low));
            Assert.That(Levels.MapAtOffset(low, -1), Is.Null);
            Assert.That(Levels.MapAtOffset(high, 1), Is.Null);
        });
    }

    [Test]
    public async Task SteppingInAHoleFallsAndHurts()
    {
        MapId low = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            low = Level();
            var high = Level();
            Levels.Combine(new[] { low, high });
            SEntMan.SpawnAtPosition("CFZLevelHole", At(high, 0, 0));
            hero = Hero(high, 3, 3);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, At(high, 0, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(hero);
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MapOf(hero), Is.EqualTo(low));
            Assert.That(Health(hero), Is.EqualTo(Hp - 5), "A 10-foot fall deals 5.");
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(hero), "Taking fall damage leaves you prone.");
        });
    }

    [TestCase(1, true)]
    [TestCase(2, false)]
    public async Task ALeapClearsAHoleUnlessItLandsInIt(int holeX, bool clears)
    {
        MapId low = default;
        MapId high = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            low = Level();
            high = Level();
            Levels.Combine(new[] { low, high });
            SEntMan.SpawnAtPosition("CFZLevelHole", At(high, holeX, 0));
            hero = Hero(high, 0, 0);
            var transform = SEntMan.System<SharedTransformSystem>();
            transform.SetWorldRotation(hero, new Vector2(1f, 0f).ToWorldAngle());
            SEntMan.System<SharedPhysicsSystem>().WakeBody(hero);
            SEntMan.System<CFActivitySystem>().Leap(hero);
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MapOf(hero), Is.EqualTo(clears ? high : low));
        });
    }

    [Test]
    public async Task FallsPassThroughOpenAir()
    {
        await Server.WaitAssertion(() =>
        {
            var bottom = Level();
            var air = Level(floor: false);
            var top = Level();
            Levels.Combine(new[] { bottom, air, top });
            var hero = Hero(top);

            Assert.That(Levels.Fall(hero), Is.EqualTo(20));
            Assert.That(MapOf(hero), Is.EqualTo(bottom));
            Assert.That(Health(hero), Is.EqualTo(Hp - 10));
        });
    }

    [Test]
    public async Task NothingBelowMeansNoFall()
    {
        await Server.WaitAssertion(() =>
        {
            var low = Level();
            Levels.Combine(new[] { low, Level() });
            var hero = Hero(low);

            Assert.That(Levels.Fall(hero), Is.Zero);
            Assert.That(Health(hero), Is.EqualTo(Hp));
        });
    }

    [Test]
    public async Task AutoStairsBuildTheirOtherEndAndCarryYouThrough()
    {
        MapId low = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            low = Level();
            var high = Level();
            Levels.Combine(new[] { low, high });

            var stairs = SEntMan.SpawnAtPosition("CFStairsDownStoneAutoLink", At(high, 0, 0));
            Assert.That(SEntMan.GetComponent<LinkedEntityComponent>(stairs).LinkedEntities, Has.Count.EqualTo(1));

            hero = Hero(high, 3, 3);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, At(high, 0, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(hero);
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MapOf(hero), Is.EqualTo(low));
            Assert.That(Health(hero), Is.EqualTo(Hp), "Stairs do not hurt.");
        });
    }

    private Entity<CFZLevelClimbComponent> Ladder(MapId high, int dc = 0)
    {
        var ladder = SEntMan.SpawnAtPosition("CFLadderDownAutoLink", At(high, 1, 1));
        var climb = SEntMan.GetComponent<CFZLevelClimbComponent>(ladder);
        climb.Dc = dc;
        return (ladder, climb);
    }

    [Test]
    public async Task LaddersClimbAndNeedFreeHands()
    {
        await Server.WaitAssertion(() =>
        {
            var low = Level();
            var high = Level();
            Levels.Combine(new[] { low, high });
            var ladder = Ladder(high);
            var hero = Hero(high, 1, 0);

            var item = SEntMan.SpawnAtPosition("CFHoodedLantern", At(high, 1, 0));
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(hero, item, checkActionBlocker: false));
            Assert.That(Levels.Climb(ladder, hero), Is.False, "Climbing takes two free hands.");
            Assert.That(MapOf(hero), Is.EqualTo(high));

            SEntMan.DeleteEntity(item);
            Assert.That(Levels.Climb(ladder, hero));
            Assert.That(MapOf(hero), Is.EqualTo(low));
        });
    }

    [Test]
    public async Task CriticalFailureOnTheWayDownFalls()
    {
        await Server.WaitAssertion(() =>
        {
            var low = Level();
            var high = Level();
            Levels.Combine(new[] { low, high });
            var ladder = Ladder(high, dc: 90);
            var hero = Hero(high, 1, 0);
            SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Strength] = -60;

            // A natural 20 lifts this to a plain failure, so allow a few tries.
            for (var i = 0; i < 20 && MapOf(hero) == high; i++)
            {
                Assert.That(Levels.Climb(ladder, hero), Is.False);
            }

            Assert.That(MapOf(hero), Is.EqualTo(low));
            Assert.That(Health(hero), Is.EqualTo(Hp - 5));
        });
    }

    [Test]
    public async Task GhostsCanShiftLevels()
    {
        await Server.WaitAssertion(() =>
        {
            var low = Level();
            var high = Level();
            Levels.Combine(new[] { low, high });
            var ghost = SEntMan.SpawnAtPosition("MobObserver", At(low, 0, 0));

            var mover = SEntMan.GetComponent<CFZLevelMoverComponent>(ghost);
            Assert.That(mover.UpActionEntity, Is.Not.Null, "The ghost should get its action buttons.");
            Assert.That(mover.DownActionEntity, Is.Not.Null);
            Assert.That(Levels.Shift(ghost, 1));
            Assert.That(MapOf(ghost), Is.EqualTo(high));
            Assert.That(Levels.Shift(ghost, 1), Is.False, "There is nothing higher.");
        });
    }

    [Test]
    public async Task AreaEffectsStayOnTheirLevel()
    {
        await Server.WaitAssertion(() =>
        {
            var low = Level();
            var high = Level();
            Levels.Combine(new[] { low, high });

            var caster = Hero(high, 3, 3);
            var sameLevel = Hero(high);
            var otherLevel = Hero(low);
            var blast = SEntMan.Spawn("CFZTestBlast");
            var xforms = SEntMan.System<SharedTransformSystem>();

            SEntMan.System<CFEffectSystem>().ResolveArea(blast, caster, xforms.ToMapCoordinates(At(high, 0, 0)));

            Assert.That(Health(sameLevel), Is.EqualTo(Hp - 5));
            Assert.That(Health(otherLevel), Is.EqualTo(Hp), "The same spot on another level is out of reach.");
        });
    }

    [Test]
    public async Task TestStationLoadsBothLevelsAndLinksThem()
    {
        await Server.WaitAssertion(() =>
        {
            var map = Server.ProtoMan.Index(new ProtoId<GameMapPrototype>("CFZLevelTest"));
            SEntMan.System<GameTicker>().LoadGameMap(map, out var ground);

            var below = Levels.MapAtOffset(ground, -1);
            Assert.That(below, Is.Not.Null, "The cellar should load under the station.");

            var maps = SEntMan.System<SharedMapSystem>();
            var cellar = maps.GetMapOrInvalid(below!.Value);
            Assert.That(SEntMan.GetComponent<MapAtmosphereComponent>(cellar).Space, Is.False, "Saved maps keep the sky air.");

            var linked = 0;
            var query = SEntMan.AllEntityQueryEnumerator<LinkedEntityComponent, TransformComponent>();
            while (query.MoveNext(out var link, out var xform))
            {
                if (xform.MapID == ground && link.LinkedEntities.Count > 0)
                    linked++;
            }

            Assert.That(linked, Is.EqualTo(2), "The stairs and the ladder both reach the cellar.");

            maps.InitializeMap(ground);
            var ends = 0;
            var built = SEntMan.AllEntityQueryEnumerator<LinkedEntityComponent, TransformComponent>();
            while (built.MoveNext(out var link, out var xform))
            {
                if (xform.MapID == below.Value && link.LinkedEntities.Count > 0)
                    ends++;
            }

            Assert.That(ends, Is.EqualTo(2), "Starting the round must not build a second set of ends.");
        });
    }
}
