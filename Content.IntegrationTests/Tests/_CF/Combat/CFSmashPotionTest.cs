using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Alchemy;
using Content.Shared.CF.Combat;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>
/// Thrown healing potions (<c>CFSmashPotion</c>): one that hits a creature heals it, and one that hits the floor
/// leaves a puddle that heals whoever lies prone in it.
/// </summary>
[TestFixture]
public sealed class CFSmashPotionTest : GameTest
{
    private const string Potion = "CFHealingPotionMinor";

    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSmashTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 100
    currentHp: 50
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private int Hp(EntityUid uid)
    {
        return SEntMan.GetComponent<CFCombatantComponent>(uid).CurrentHp;
    }

    private int HealingPuddles()
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<CFHealingPuddleComponent>();
        while (query.MoveNext(out _, out _))
        {
            count++;
        }

        return count;
    }

    [Test]
    public async Task ThrownAtACreatureItBreaksAndHeals()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid thrower = default;
        EntityUid friend = default;
        EntityUid potion = default;
        await Server.WaitPost(() =>
        {
            thrower = SEntMan.SpawnAttachedTo("CFSmashTestHero", _map.Local(0, 0));
            friend = SEntMan.SpawnAttachedTo("CFSmashTestHero", _map.Local(3, 0));
            potion = SEntMan.SpawnAttachedTo(Potion, _map.Local(0, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(friend);
            SEntMan.System<ThrowingSystem>().TryThrow(potion, new Vector2(5f, 0f), user: thrower);
        });

        await RunSeconds(2f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.Deleted(potion), "The bottle breaks.");
                Assert.That(Hp(friend), Is.InRange(51, 58), "1d8 restored.");
                Assert.That(Hp(thrower), Is.EqualTo(50), "Not the thrower.");
            });
        });
    }

    [Test]
    public async Task OnTheFloorItLeavesAPuddleToRollIn()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default;
        EntityUid potion = default;
        var before = 0;
        await Server.WaitPost(() =>
        {
            before = HealingPuddles();
            hero = SEntMan.SpawnAttachedTo("CFSmashTestHero", _map.Local(0, 0));
            potion = SEntMan.SpawnAttachedTo(Potion, _map.Local(0, 0));
            SEntMan.System<ThrowingSystem>().TryThrow(potion, new Vector2(3f, 0f), user: hero);
        });

        await RunSeconds(3f);

        var spilled = 0;
        var hpStanding = 0;
        await Server.WaitPost(() =>
        {
            spilled = HealingPuddles() - before;
            var query = SEntMan.AllEntityQueryEnumerator<CFHealingPuddleComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, xform.Coordinates);
            }
        });

        await RunSeconds(1f);
        await Server.WaitPost(() =>
        {
            hpStanding = Hp(hero);
            SEntMan.System<CFConditionSystem>().KnockProne(hero);
        });

        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.Deleted(potion), "The bottle breaks.");
                Assert.That(spilled, Is.EqualTo(1), "It leaves a puddle.");
                Assert.That(hpStanding, Is.EqualTo(50), "Standing in it does nothing.");
                Assert.That(Hp(hero), Is.InRange(51, 58), "Prone in it: 1d8 restored.");
                Assert.That(HealingPuddles() - before, Is.Zero, "And the puddle is used up.");
            });
        });
    }
}
