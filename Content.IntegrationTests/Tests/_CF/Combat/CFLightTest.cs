using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Lighting;
using Content.Shared.CF.Preferences;
using Content.Shared.Light.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Bright light, dim light and darkness, and what each does to eyes without the sense for it.</summary>
[TestFixture]
[TestOf(typeof(CFLightSystem))]
public sealed class CFLightTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    // Dirty, because the test turns the light rules on and darkens the map.
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: CFLightTestLamp
  components:
  - type: PointLight
    radius: 4
    energy: 2

- type: entity
  parent: MobHuman
  id: CFLightTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 200
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFLightTestHero
  id: CFLightTestFoe
  components:
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFLightTestFoe
  id: CFLightTestFoeLowLight
  components:
  - type: CFSenses
    lowLightVision: true

- type: entity
  parent: CFLightTestFoe
  id: CFLightTestFoeDarkvision
  components:
  - type: CFSenses
    darkvision: true
";

    private CFDevMap _map = default!;

    private CFLightSystem Light => SEntMan.System<CFLightSystem>();

    private EntityUid Spawn(string id, int x, int y = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, y));
    }

    /// <summary>No sun, no ambient light and none of the arena's own lamps: only what the test lights.</summary>
    private void Darken()
    {
        Server.CfgMan.SetCVar(CFCVars.LightRules, true);
        var map = SEntMan.System<SharedMapSystem>().GetMap(_map.MapId);
        SEntMan.RemoveComponent<LightCycleComponent>(map);
        SEntMan.System<SharedMapSystem>().SetAmbientLight(_map.MapId, Color.Black);

        var lights = SEntMan.System<PointLightSystem>();
        var query = SEntMan.AllEntityQueryEnumerator<PointLightComponent>();
        while (query.MoveNext(out var uid, out var light))
        {
            lights.SetEnabled(uid, false, light);
        }

        Light.Forget();
    }

    [Test]
    public async Task ALampIsBrightNearDimFartherAndAWallStopsIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            Darken();
            var near = Spawn("CFLightTestHero", 1);
            var mid = Spawn("CFLightTestHero", 3);
            var far = Spawn("CFLightTestHero", 6);
            Assert.That(Light.LevelAt(near), Is.EqualTo(CFLightLevel.Darkness), "Nothing is lit yet.");

            Spawn("CFLightTestLamp", 0);
            Light.Forget();
            Assert.Multiple(() =>
            {
                Assert.That(Light.LevelAt(near), Is.EqualTo(CFLightLevel.Bright));
                Assert.That(Light.LevelAt(mid), Is.EqualTo(CFLightLevel.Dim));
                Assert.That(Light.LevelAt(far), Is.EqualTo(CFLightLevel.Darkness));
            });

            SEntMan.SpawnAtPosition("WallSolid", _map.Local(2, 0));
            Light.Forget();
            Assert.That(Light.LevelAt(mid), Is.EqualTo(CFLightLevel.Darkness), "The wall stands between the lamp and the creature.");

            Server.CfgMan.SetCVar(CFCVars.LightRules, false);
            Assert.That(Light.LevelAt(far), Is.EqualTo(CFLightLevel.Bright), "With the rules off, everywhere is bright.");
        });
    }

    [Test]
    public async Task DimLightConcealsAndDarknessHidesFromEyesWithoutTheSense()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            Darken();
            Spawn("CFLightTestLamp", 0);
            var lit = Spawn("CFLightTestHero", 1);
            var dim = Spawn("CFLightTestHero", 3);
            var dark = Spawn("CFLightTestHero", 6);
            var plain = Spawn("CFLightTestFoe", 4, 1);
            var lowLight = Spawn("CFLightTestFoeLowLight", 5, 1);
            var darkvision = Spawn("CFLightTestFoeDarkvision", 6, 1);
            Light.Forget();

            Assert.Multiple(() =>
            {
                Assert.That(Light.Obscured(lit, plain), Is.EqualTo(CFObscured.None));
                Assert.That(Light.Obscured(dim, plain), Is.EqualTo(CFObscured.Concealed));
                Assert.That(Light.Obscured(dark, plain), Is.EqualTo(CFObscured.Hidden));

                Assert.That(Light.Obscured(dim, lowLight), Is.EqualTo(CFObscured.None), "Low-light vision sees in dim light.");
                Assert.That(Light.Obscured(dark, lowLight), Is.EqualTo(CFObscured.Hidden));

                Assert.That(Light.Obscured(dim, darkvision), Is.EqualTo(CFObscured.None));
                Assert.That(Light.Obscured(dark, darkvision), Is.EqualTo(CFObscured.None), "Darkvision sees in darkness.");
            });

            Server.CfgMan.SetCVar(CFCVars.LightRules, false);
        });
    }

    [Test]
    public async Task ACreatureCanHideInTheDarkWithNoCover()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            Darken();
            var stealth = SEntMan.System<CFStealthSystem>();
            var hero = Spawn("CFLightTestHero", 0);
            SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Dexterity] = Extreme;
            var plain = Spawn("CFLightTestFoe", 3);

            Assert.That(stealth.CoverBonus(hero, plain), Is.Zero);
            Assert.That(stealth.TryHide(hero), "Darkness is enough to hide in from eyes that cannot see in it.");
            Assert.That(stealth.Detection(hero, plain), Is.EqualTo(CFDetection.Hidden));

            var seer = Spawn("CFLightTestHero", 0, 1);
            SEntMan.GetComponent<CFAbilityScoreComponent>(seer).AbilityModifiers[CFAbility.Dexterity] = Extreme;
            SEntMan.DeleteEntity(plain);
            Spawn("CFLightTestFoeDarkvision", 3, 1);
            Assert.That(stealth.TryHide(seer), Is.False, "Darkvision sees it standing in the open.");

            Server.CfgMan.SetCVar(CFCVars.LightRules, false);
        });
    }
}
