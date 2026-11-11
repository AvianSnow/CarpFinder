using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>The Multiple Attack Penalty the HUD shows for what is in hand.</summary>
[TestFixture]
[TestOf(typeof(CFStrikeSystem))]
public sealed class CFMapPenaltyTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFMapHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 20
    currentHp: 20
  - type: CFAbilityScore
  - type: CFCombatOptions
";

    private CFDevMap _map = default!;

    private CFStrikeSystem Strikes => SEntMan.System<CFStrikeSystem>();

    /// <summary>A hero that has already made <paramref name="attacks"/> attacks, holding <paramref name="item"/>.</summary>
    private EntityUid Hero(int attacks, string item = null)
    {
        var hero = SEntMan.SpawnEntity("CFMapHero", _map.Local(0, 0));
        SEntMan.EnsureComponent<CFActionPoolComponent>(hero).MapAttacks = attacks;
        if (item == null)
            return hero;

        var uid = SEntMan.SpawnEntity(item, _map.Local(0, 0));
        var hands = SEntMan.System<SharedHandsSystem>();
        Assert.That(hands.TryPickupAnyHand(hero, uid, checkActionBlocker: false));
        Assert.That(hands.TryGetActiveItem(hero, out var held) && held == uid, "The item has to be the one in use.");
        return hero;
    }

    [Test]
    public async Task PenaltyFollowsTheWeaponInHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Strikes.NextStrikeMapPenalty(Hero(0, "CFLongsword")), Is.Zero, "The first Strike has no penalty.");

            Assert.That(Strikes.NextStrikeMapPenalty(Hero(1, "CFLongsword")), Is.EqualTo(-5));
            Assert.That(Strikes.NextStrikeMapPenalty(Hero(2, "CFLongsword")), Is.EqualTo(-10));
            Assert.That(Strikes.NextStrikeMapPenalty(Hero(5, "CFLongsword")), Is.EqualTo(-10), "It stops at the third step.");

            Assert.That(Strikes.NextStrikeMapPenalty(Hero(1, "CFDagger")), Is.EqualTo(-4), "A dagger is agile.");
            Assert.That(Strikes.NextStrikeMapPenalty(Hero(2, "CFDagger")), Is.EqualTo(-8));

            Assert.That(Strikes.NextStrikeMapPenalty(Hero(1)), Is.EqualTo(-4), "Fists are agile.");
            Assert.That(Strikes.NextStrikeMapPenalty(Hero(1, "CFLongbow")), Is.EqualTo(-5), "A bow is not fists.");
        });
    }
}
