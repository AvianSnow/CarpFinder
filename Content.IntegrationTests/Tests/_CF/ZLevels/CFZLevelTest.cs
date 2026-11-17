using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.CF.ZLevels.Core;
using Content.Server.CF.ZLevels.Portals;
using Content.Shared.GameTicking;
using Content.Server.Maps;
using Content.Shared.Atmos.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.ZLevels;
using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Ghost;
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

/// <summary>
/// Stacked maps, falling through open air, stairs, ladders and holes. Levels are small bare maps built here and
/// joined into one network, as the station's own are at round start.
/// </summary>
[TestFixture]
[TestOf(typeof(CFZLevelsSystem))]
[TestOf(typeof(CFZLevelPortalSystem))]
public sealed class CFZLevelTest : GameTest
{
    private const int Hp = 500;

    /// <summary>A fall of one level is 10 feet, which is 5 damage.</summary>
    private const int OneLevel = 5;

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

    private CFZLevelsSystem Levels => SEntMan.System<CFZLevelsSystem>();
    private CFZLevelPortalSystem Portals => SEntMan.System<CFZLevelPortalSystem>();
    private SharedMapSystem Maps => SEntMan.System<SharedMapSystem>();

    /// <summary>
    /// A level with a square of floor <paramref name="half"/> tiles each way from the middle, or none at all for open
    /// air. A hero with no player has no mover friction, so after a leap it slides on for as long as the test runs,
    /// and off the edge of the floor it would fall: the leap test asks for a wide one.
    /// </summary>
    private MapId Level(bool floor = true, int half = 4)
    {
        Maps.CreateMap(out var id);
        var grid = Maps.CreateGridEntity(id);
        var plating = Server.ResolveDependency<ITileDefinitionManager>()["Plating"];
        for (var x = -half; x <= half; x++)
        {
            for (var y = -half; y <= half; y++)
            {
                // A level of open air keeps one far tile so the grid exists.
                var open = !floor && (x, y) != (half, half);
                if (!open)
                    Maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }
        }

        _levels.Add((id, grid));
        return id;
    }

    /// <summary>Joins levels into one network, from the lowest up.</summary>
    private void Combine(params MapId[] maps)
    {
        var network = Levels.CreateMapNetwork();
        var depths = new Dictionary<EntityUid, int>();
        for (var i = 0; i < maps.Length; i++)
        {
            depths[Maps.GetMapOrInvalid(maps[i])] = i;
        }

        Assert.That(Levels.TryAddMapsIntoNetwork(network, depths));
    }

    /// <summary>The map so many levels from this one, or null.</summary>
    private MapId? Offset(MapId map, int offset)
    {
        if (!Levels.TryMapOffset(Maps.GetMapOrInvalid(map), offset, out _, out var found))
            return null;

        return found.MapId;
    }

    private EntityCoordinates At(MapId map, int x, int y)
    {
        var (_, grid) = _levels.Find(l => l.Map == map);
        return Maps.GridTileToLocal(grid.Owner, grid.Comp, new Vector2i(x, y));
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

    [TestCase(1f, 10)]
    [TestCase(2f, 20)]
    [TestCase(0.95f, 10)]
    public void TheLandingSpeedSaysHowFarItFell(float levels, int feet)
    {
        var speed = MathF.Sqrt(2 * 9.8f * levels);
        Assert.That(CFFallRules.FeetFromImpact(speed, 9.8f), Is.EqualTo(feet));
    }

    [Test]
    public async Task LevelsKnowTheirNeighbours()
    {
        await Server.WaitAssertion(() =>
        {
            var low = Level();
            var high = Level();
            Combine(low, high);

            Assert.That(Offset(low, 1), Is.EqualTo(high));
            Assert.That(Offset(high, -1), Is.EqualTo(low));
            Assert.That(Offset(low, -1), Is.Null);
            Assert.That(Offset(high, 1), Is.Null);
            Assert.That(SEntMan.GetComponent<CFZMapComponent>(Maps.GetMapOrInvalid(high)).Depth, Is.EqualTo(1));
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
            Combine(low, high);
            SEntMan.SpawnAtPosition("CFZLevelHole", At(high, 0, 0));
            hero = Hero(high, 3, 3);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, At(high, 0, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(hero);
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MapOf(hero), Is.EqualTo(low));
            Assert.That(Health(hero), Is.EqualTo(Hp - OneLevel), "A 10-foot fall deals 5.");
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
            low = Level(half: 40);
            high = Level(half: 40);
            Combine(low, high);
            SEntMan.SpawnAtPosition("CFZLevelHole", At(high, holeX, 0));
            hero = Hero(high, 0, 0);
            var transform = SEntMan.System<SharedTransformSystem>();
            transform.SetWorldRotation(hero, new Vector2(1f, 0f).ToWorldAngle());
            SEntMan.System<SharedPhysicsSystem>().WakeBody(hero);
            SEntMan.System<CFActivitySystem>().Leap(hero);
        });

        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MapOf(hero), Is.EqualTo(clears ? high : low));
        });
    }

    [Test]
    public async Task FallsPassThroughOpenAir()
    {
        MapId bottom = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            bottom = Level();
            var air = Level(floor: false);
            var sky = Level(floor: false);
            Combine(bottom, air, sky);
            hero = Hero(sky);
        });

        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MapOf(hero), Is.EqualTo(bottom));
            Assert.That(Health(hero), Is.EqualTo(Hp - 2 * OneLevel), "Two levels is 20 feet, which is 10 damage.");
        });
    }

    [Test]
    public async Task LooseFurnitureFallsThroughOpenAir()
    {
        MapId bottom = default;
        MapId air = default;
        EntityUid overAir = default;
        EntityUid onFloor = default;
        await Server.WaitPost(() =>
        {
            bottom = Level();
            air = Level(floor: false);
            Combine(bottom, air);
            overAir = SEntMan.SpawnAtPosition("CFSmallWoodenCrate", At(air, 0, 0));
            onFloor = SEntMan.SpawnAtPosition("CFSmallWoodenCrate", At(bottom, 2, 2));
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MapOf(overAir), Is.EqualTo(bottom), "A crate over nothing falls to the floor below.");
            Assert.That(MapOf(onFloor), Is.EqualTo(bottom), "A crate on a floor stays where it is.");
            Assert.That(SEntMan.GetComponent<CFZPhysicsComponent>(overAir).LocalPosition, Is.EqualTo(0f).Within(0.05f), "It comes to rest on the floor.");
        });
    }

    [Test]
    public async Task NothingBelowMeansNoFall()
    {
        MapId air = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            air = Level(floor: false);
            Combine(air, Level());
            hero = Hero(air);
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MapOf(hero), Is.EqualTo(air), "With no level below there is nowhere to fall to.");
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
            Combine(low, high);

            var stairs = SEntMan.SpawnAtPosition("CFStairsDownStoneAutoLink", At(high, 0, 0));
            Assert.That(SEntMan.GetComponent<LinkedEntityComponent>(stairs).LinkedEntities, Has.Count.EqualTo(1));

            hero = Hero(high, 3, 3);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, At(high, 0, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(hero);
        });

        await Pair.RunTicksSync(30);

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
            Combine(low, high);
            var ladder = Ladder(high);
            var hero = Hero(high, 1, 0);

            var item = SEntMan.SpawnAtPosition("CFHoodedLantern", At(high, 1, 0));
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(hero, item, checkActionBlocker: false));
            Assert.That(Portals.Climb(ladder, hero), Is.False, "Climbing takes two free hands.");
            Assert.That(MapOf(hero), Is.EqualTo(high));

            SEntMan.DeleteEntity(item);
            Assert.That(Portals.Climb(ladder, hero));
            Assert.That(MapOf(hero), Is.EqualTo(low));
        });
    }

    [Test]
    public async Task CriticalFailureOnTheWayDownFalls()
    {
        MapId low = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            low = Level();
            var high = Level();
            Combine(low, high);
            var ladder = Ladder(high, dc: 90);
            hero = Hero(high, 1, 0);
            SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Strength] = -60;

            // A natural 20 lifts this to a plain failure, so allow a few tries.
            for (var i = 0; i < 20 && MapOf(hero) == high; i++)
            {
                Assert.That(Portals.Climb(ladder, hero), Is.False);
            }

            Assert.That(MapOf(hero), Is.EqualTo(low), "A critical failure on the way down is a fall.");
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Health(hero), Is.EqualTo(Hp - OneLevel));
        });
    }

    [Test]
    public async Task GhostsCanShiftLevels()
    {
        await Server.WaitAssertion(() =>
        {
            var low = Level();
            var high = Level();
            Combine(low, high);
            var ghost = SEntMan.SpawnAtPosition("MobObserver", At(low, 0, 0));

            var mover = SEntMan.GetComponent<CFZLevelGhostMoverComponent>(ghost);
            Assert.That(mover.ZLevelUpActionEntity, Is.Not.Null, "The ghost should get its action buttons.");
            Assert.That(mover.ZLevelDownActionEntity, Is.Not.Null);
            Assert.That(Levels.TryMoveUp(ghost));
            Assert.That(MapOf(ghost), Is.EqualTo(high));
            Assert.That(Levels.TryMoveUp(ghost), Is.False, "There is nothing higher.");
        });
    }

    [Test]
    public async Task AreaEffectsStayOnTheirLevel()
    {
        await Server.WaitAssertion(() =>
        {
            var low = Level();
            var high = Level();
            Combine(low, high);

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

            var below = Offset(ground, -1);
            Assert.That(below, Is.Not.Null, "The cellar should load under the station.");

            var cellar = Maps.GetMapOrInvalid(below!.Value);
            Assert.That(SEntMan.GetComponent<MapAtmosphereComponent>(cellar).Space, Is.False, "Saved maps keep the sky air.");

            var linked = 0;
            var query = SEntMan.AllEntityQueryEnumerator<LinkedEntityComponent, TransformComponent>();
            while (query.MoveNext(out var link, out var xform))
            {
                if (xform.MapID == ground && link.LinkedEntities.Count > 0)
                    linked++;
            }

            Assert.That(linked, Is.EqualTo(2), "The stairs and the ladder both reach the cellar.");

            Maps.InitializeMap(ground);
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
