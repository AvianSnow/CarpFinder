using System;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Touch spells reach the next tile in every direction, and Reach Spell sends them further.</summary>
[TestFixture]
[TestOf(typeof(CFEffectSystem))]
public sealed class CFTouchRangeTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -2, 8, 3);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFTouchHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
";

    private CFDevMap _map = default!;

    private EntityUid Spawn(int x, int y) => SEntMan.SpawnEntity("CFTouchHero", _map.Local(x, y));

    [Test]
    public async Task TouchReachesTheNextTileAndNoFurther()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool beside = false, diagonal = false, twoAway = true;
        await Server.WaitPost(() =>
        {
            var effects = SEntMan.System<CFEffectSystem>();
            var spell = SEntMan.SpawnEntity("CFLayOnHands", MapCoordinates.Nullspace);
            var hero = Spawn(0, 0);
            beside = effects.InRange(spell, hero, Spawn(1, 0));
            diagonal = effects.InRange(spell, hero, Spawn(1, 1));
            twoAway = effects.InRange(spell, hero, Spawn(2, 0));
        });

        Assert.Multiple(() =>
        {
            Assert.That(beside, Is.True);
            Assert.That(diagonal, Is.True);
            Assert.That(twoAway, Is.False);
        });
    }

    [Test]
    public async Task ReachSpellMakesTouchThirtyFeet()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool near = false, far = true;
        await Server.WaitPost(() =>
        {
            var effects = SEntMan.System<CFEffectSystem>();
            var spell = SEntMan.SpawnEntity("CFLayOnHands", MapCoordinates.Nullspace);
            var hero = Spawn(0, 0);
            var shape = SEntMan.EnsureComponent<CFActiveSpellshapeComponent>(hero);
            shape.Shape = "CFActReachSpell";
            shape.Expires = Server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(60);
            near = effects.InRange(spell, hero, Spawn(6, 0));
            far = effects.InRange(spell, hero, Spawn(7, 0));
        });

        Assert.Multiple(() =>
        {
            Assert.That(near, Is.True, "30 feet is 6 tiles.");
            Assert.That(far, Is.False);
        });
    }
}
