using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Content.Shared.CF.Terrain;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Difficult terrain: what makes it, how much it slows, and the options that ignore it.</summary>
[TestFixture]
[TestOf(typeof(CFTerrainSystem))]
public sealed class CFTerrainTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFTerrainTestHero
  components:
  - type: CFCombatant
    level: 5
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFTerrainTestHero
  id: CFTerrainTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFTerrainSystem Terrain => SEntMan.System<CFTerrainSystem>();

    private EntityUid Spawn(int x, string id = "CFTerrainTestHero")
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private void Give(EntityUid mob, string feat)
    {
        SEntMan.System<CFCharacterFeatureSystem>().Apply(mob, Server.ProtoMan.Index<CFFeatPrototype>(feat).Grants, $"feat:{feat}");
    }

    private float Speed(EntityUid mob)
    {
        Terrain.Recheck(mob);
        return SEntMan.GetComponent<MovementSpeedModifierComponent>(mob).SprintSpeedModifier;
    }

    [Test]
    public async Task BushesSlowByHalfAndLeafWallsByTwoThirds()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        float open = 0, bush = 0, leaves = 0, halfling = 0, halflingLeaves = 0, dwarf = 0;
        await Server.WaitPost(() =>
        {
            open = Speed(Spawn(0));

            SEntMan.SpawnEntity("CFHighBush", _map.Local(1, 0));
            bush = Speed(Spawn(1));

            SEntMan.SpawnEntity("CFWallLeaf", _map.Local(2, 0));
            leaves = Speed(Spawn(2));

            var wildwood = Spawn(1);
            Give(wildwood, "CFForestPassage");
            halfling = Speed(wildwood);

            var throughLeaves = Spawn(2);
            Give(throughLeaves, "CFForestPassage");
            halflingLeaves = Speed(throughLeaves);

            var rockRunner = Spawn(1);
            Give(rockRunner, "CFRockRunner");
            dwarf = Speed(rockRunner);
        });

        Assert.Multiple(() =>
        {
            Assert.That(open, Is.EqualTo(1f).Within(0.01f));
            Assert.That(bush, Is.EqualTo(0.5f).Within(0.01f), "A bush is difficult terrain.");
            Assert.That(leaves, Is.EqualTo(1f / 3f).Within(0.01f), "A leaf wall is greater difficult terrain.");
            Assert.That(halfling, Is.EqualTo(1f).Within(0.01f), "Ignoring plants ignores the bush.");
            Assert.That(halflingLeaves, Is.EqualTo(0.5f).Within(0.01f), "Ignoring makes greater difficult terrain only difficult.");
            Assert.That(dwarf, Is.EqualTo(0.5f).Within(0.01f), "Rock Runner only ignores stone.");
        });
    }

    [Test]
    public async Task DeepSnowTilesAreDifficult()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        float snow = 0, deeper = 0;
        await Server.WaitPost(() =>
        {
            var maps = SEntMan.System<SharedMapSystem>();
            var grid = SEntMan.GetComponent<MapGridComponent>(_map.Grid);
            var tiles = Server.ResolveDependency<ITileDefinitionManager>();
            maps.SetTile(_map.Grid, grid, _map.Origin, new Tile(tiles["CFFloorSnowDeep"].TileId));
            maps.SetTile(_map.Grid, grid, _map.Origin + new Vector2i(1, 0), new Tile(tiles["CFFloorSnowDeepDeep"].TileId));
            snow = Speed(Spawn(0));
            deeper = Speed(Spawn(1));
        });

        Assert.Multiple(() =>
        {
            Assert.That(snow, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(deeper, Is.EqualTo(1f / 3f).Within(0.01f));
        });
    }

    [Test]
    public async Task NoStepInDifficultTerrainWithoutFeatherStep()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool open = false, plain = true, feather = false, lightPaws = false;
        float pawsSpeed = 0;
        await Server.WaitPost(() =>
        {
            open = Terrain.CanStep(Spawn(0));

            SEntMan.SpawnEntity("CFHighBush", _map.Local(1, 0));
            var walker = Spawn(1);
            Terrain.Recheck(walker);
            plain = Terrain.CanStep(walker);

            var nimble = Spawn(1);
            Give(nimble, "CFFeatherStep");
            Terrain.Recheck(nimble);
            feather = Terrain.CanStep(nimble);

            var cat = Spawn(1);
            Terrain.IgnoreAllFor(cat, 6f);
            pawsSpeed = Speed(cat);
            lightPaws = Terrain.CanStep(cat);
        });

        Assert.Multiple(() =>
        {
            Assert.That(open, Is.True);
            Assert.That(plain, Is.False, "A Step cannot be taken in difficult terrain.");
            Assert.That(feather, Is.True);
            Assert.That(lightPaws, Is.True, "Light Paws ignores the terrain, so its Step is fine.");
            Assert.That(pawsSpeed, Is.EqualTo(1f).Within(0.01f));
        });
    }

    [Test]
    public async Task DragonStanceCrossesTheFirstSquareFreely()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid monk = default;
        float first = 0;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity("CFHighBush", _map.Local(1, 0));
            monk = Spawn(1);
            var stance = SEntMan.EnsureComponent<CFActiveStanceComponent>(monk);
            stance.Stance = "CFActDragonStance";
            first = Speed(monk);
        });

        await Pair.RunSeconds(1f);
        var later = 0f;
        await Server.WaitPost(() => later = Speed(monk));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(1f).Within(0.01f), "The first square is free.");
            Assert.That(later, Is.EqualTo(0.5f).Within(0.01f), "After that the bush slows the monk.");
        });
    }

    [Test]
    public async Task HamperingStanceSlowsEnemiesBesideTheGuardian()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid foe = default, ally = default, far = default;
        await Server.WaitPost(() =>
        {
            var guardian = Spawn(0);
            Give(guardian, "CFHamperingStance");
            SEntMan.EnsureComponent<CFActiveStanceComponent>(guardian).Stance = "CFActHamperingStance";
            foe = Spawn(1, "CFTerrainTestFoe");
            ally = Spawn(1);
            far = Spawn(3, "CFTerrainTestFoe");
        });

        await Pair.RunSeconds(1f);
        float foeSpeed = 0, allySpeed = 0, farSpeed = 0;
        await Server.WaitPost(() =>
        {
            foeSpeed = Speed(foe);
            allySpeed = Speed(ally);
            farSpeed = Speed(far);
        });

        Assert.Multiple(() =>
        {
            Assert.That(foeSpeed, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(allySpeed, Is.EqualTo(1f).Within(0.01f), "Allies cross freely.");
            Assert.That(farSpeed, Is.EqualTo(1f).Within(0.01f), "Only the 5 feet around the guardian.");
        });
    }

    [Test]
    public async Task SnowdriftSpellLeavesSnowAroundTheTarget()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var snow = 0;
        float speed = 0;
        await Server.WaitPost(() =>
        {
            var druid = Spawn(-1);
            var foe = Spawn(1, "CFTerrainTestFoe");
            var frostbite = SEntMan.SpawnEntity("CFFrostbite", MapCoordinates.Nullspace);
            var active = SEntMan.EnsureComponent<CFActiveSpellshapeComponent>(druid);
            active.Shape = "CFActSnowdriftSpell";

            var spellshapes = SEntMan.System<CFSpellshapeSystem>();
            spellshapes.NoteCast(druid, SEntMan.GetComponent<CFSpellComponent>(frostbite));
            spellshapes.NoteAffected(druid, frostbite, foe);

            var lookup = SEntMan.System<EntityLookupSystem>();
            snow = lookup.GetEntitiesInRange(_map.Local(1, 0), 2f, LookupFlags.All)
                .Count(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "CFFillSnowdrift");
            speed = Speed(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(snow, Is.EqualTo(9), "The target's tile and the eight around it.");
            Assert.That(speed, Is.EqualTo(0.5f).Within(0.01f));
        });
    }
}
