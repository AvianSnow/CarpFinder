using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Magic;
using Content.Server.Decals;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Magic;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Magic;

/// <summary>Figment, Sigil and Know the Way, cast through their real spell actions.</summary>
[TestFixture]
[TestOf(typeof(CFFigmentSystem))]
public sealed class CFUtilityCantripTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCantripTestCaster
  components:
  - type: CFCombatant
    level: 1
  - type: CFAbilityScore
";

    private CFDevMap _map = default!;

    private CFEffectSystem Effects => SEntMan.System<CFEffectSystem>();

    private EntityUid Caster() => SEntMan.SpawnEntity("CFCantripTestCaster", _map.Local(0, 0));

    private EntityUid Spell(string id) => SEntMan.SpawnEntity(id, MapCoordinates.Nullspace);

    private MapCoordinates Aim(int x) => SEntMan.System<SharedTransformSystem>().ToMapCoordinates(_map.Local(x, 0));

    [Test]
    public async Task FigmentIsAnItemThatVanishesWhenTouched()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid image = default;
        var picked = true;
        var pool = 0;
        await Server.WaitPost(() =>
        {
            var caster = Caster();
            pool = SEntMan.System<CFFigmentSystem>().Pool().Count;
            Effects.ResolveArea(Spell("CFFigment"), caster, Aim(2));

            var query = SEntMan.AllEntityQueryEnumerator<CFIllusionComponent>();
            Assert.That(query.MoveNext(out image, out _), "Nothing was conjured.");
            // The pickup attempt event comes from the action blocker, as it does for a player.
            picked = SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(caster, image);
        });

        await RunTicksSync(5);

        Assert.Multiple(() =>
        {
            Assert.That(pool, Is.GreaterThan(5));
            Assert.That(picked, Is.False);
            Assert.That(SEntMan.Deleted(image), Is.True, "Touching it shows it for what it is.");
        });
    }

    [Test]
    public async Task SigilPaintsOneRuneAndMovesIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int first = 0, firstAfter = 0, second = 0;
        await Server.WaitPost(() =>
        {
            var caster = Caster();
            var decals = SEntMan.System<DecalSystem>();
            int RunesAt(int x)
            {
                var spot = _map.Local(x, 0).Position;
                return decals.GetDecalsInRange(_map.Grid, spot, 0.4f, decal => CFSigilSystem.Runes.Contains(decal.Id)).Count;
            }

            Effects.ResolveArea(Spell("CFSigil"), caster, Aim(1));
            first = RunesAt(1);

            Effects.ResolveArea(Spell("CFSigil"), caster, Aim(3));
            firstAfter = RunesAt(1);
            second = RunesAt(3);
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(1));
            Assert.That(firstAfter, Is.Zero, "A caster has one sigil at a time.");
            Assert.That(second, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task KnowTheWayPutsAnArrowNorthPointingNorth()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var found = false;
        Angle rotation = default;
        float north = 0;
        await Server.WaitPost(() =>
        {
            var caster = Caster();
            var transform = SEntMan.System<SharedTransformSystem>();
            Effects.ResolveTarget(Spell("CFKnowTheWay"), caster, caster);

            var casterAt = transform.GetMapCoordinates(caster);
            var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID != "CFNorthArrow")
                    continue;

                found = true;
                rotation = transform.GetWorldRotation(uid);
                north = transform.GetMapCoordinates(uid).Position.Y - casterAt.Position.Y;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(found);
            Assert.That(rotation.Degrees, Is.EqualTo(180).Within(0.01), "The sprite points down, so it is turned around.");
            Assert.That(north, Is.EqualTo(1f).Within(0.01f));
        });
    }
}
