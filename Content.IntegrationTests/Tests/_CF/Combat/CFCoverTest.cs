using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Cover;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Cover from walls and from creatures in the way, and Take Cover.</summary>
[TestFixture]
[TestOf(typeof(CFCoverSystem))]
public sealed class CFCoverTest : GameTest
{
    /// <summary>Half a tile is 0.5, so a creature this far up its tile has its centre line just inside a wall on the row.</summary>
    private const float CornerOffset = 0.45f;

    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCoverTestHero
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
  parent: CFCoverTestHero
  id: CFCoverTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCoverSystem Cover => SEntMan.System<CFCoverSystem>();
    private CFActionPoolSystem Actions => SEntMan.System<CFActionPoolSystem>();

    private EntityUid Spawn(string id, int x, float up = 0f)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0).Offset(new Vector2(0f, up)));
    }

    private void Wall(int x)
    {
        SEntMan.SpawnAtPosition("WallSolid", _map.Local(x, 0));
    }

    [Test]
    public async Task ACreatureInTheWayIsLesserCover()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFCoverTestHero", 0);
            var foe = Spawn("CFCoverTestFoe", 4);
            Assert.That(Cover.Cover(hero, foe), Is.EqualTo(CFCover.None));
            Assert.That(Cover.AcBonus(hero, foe, ranged: true), Is.Zero);
            Assert.That(Cover.CanTakeCover(hero), Is.False, "Nothing to take cover behind.");

            Spawn("CFCoverTestFoe", 2);
            Assert.That(Cover.Cover(hero, foe), Is.EqualTo(CFCover.Lesser));
            Assert.That(Cover.AcBonus(hero, foe, ranged: true), Is.EqualTo(1));
            Assert.That(Cover.AcBonus(hero, foe, ranged: true, ignoreLesser: true), Is.Zero);
            Assert.That(Cover.StealthBonus(hero, foe), Is.Zero, "A creature is nothing to hide behind.");
            Assert.That(Cover.CanTakeCover(hero), Is.False);
        });
    }

    [Test]
    public async Task AWallIsGreaterCoverAndItDoesNotAddToOtherBonuses()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFCoverTestHero", 0);
            var foe = Spawn("CFCoverTestFoe", 4);
            Wall(2);

            Assert.That(Cover.Cover(hero, foe), Is.EqualTo(CFCover.Greater));
            Assert.That(Cover.AcBonus(hero, foe, ranged: true), Is.EqualTo(4));
            Assert.That(Cover.StealthBonus(hero, foe), Is.EqualTo(4));
            Assert.That(Cover.ReflexBonus(hero, SEntMan.System<SharedTransformSystem>().GetMapCoordinates(foe)), Is.EqualTo(4));

            // A circumstance bonus of 2 leaves 2 of the wall's 4.
            SEntMan.System<CFBonusSystem>().AddBonus(hero, CFBonusStat.ArmorClass, CFBonusType.Circumstance, 2, 60f);
            Assert.That(Cover.AcBonus(hero, foe, ranged: true), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TakeCoverTurnsACornerIntoGreaterCoverUntilAnAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            // Both stand high on their tiles, so the wall on the row stops the centre line and one side.
            var hero = Spawn("CFCoverTestHero", 0, CornerOffset);
            var foe = Spawn("CFCoverTestFoe", 4, CornerOffset);
            Wall(2);

            Assert.That(Cover.Cover(hero, foe), Is.EqualTo(CFCover.Standard));
            Assert.That(Cover.AcBonus(hero, foe, ranged: true), Is.EqualTo(2));

            Assert.That(Cover.TryTakeCover(hero));
            Assert.That(SEntMan.GetComponent<CFActionPoolComponent>(hero).Stamina, Is.EqualTo(2f), "Take Cover is 1 action.");
            Assert.That(Cover.Cover(hero, foe), Is.EqualTo(CFCover.Greater));
            Assert.That(Cover.TryTakeCover(hero), Is.False, "It is already taken.");

            Actions.CountAttack(hero);
            Assert.That(Cover.Cover(hero, foe), Is.EqualTo(CFCover.Standard), "An attack roll ends it.");
        });
    }
}
