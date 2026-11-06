using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.Atmos.EntitySystems;
using Content.Server.CF.Temperature;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Stock fire and heat have to land on CF hit points, or a creature dies with all of them left.</summary>
[TestFixture]
[TestOf(typeof(CFStockHeatSystem))]
public sealed class CFStockHeatTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFStockHeatTestMob
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions

- type: entity
  parent: CFStockHeatTestMob
  id: CFStockHeatTestFrail
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 2
    currentHp: 2
    diesAtZeroHp: true
";

    private CFDevMap _map = default!;

    private EntityUid Burning(string id, float stacks = 10f)
    {
        var mob = SEntMan.SpawnEntity(id, _map.Local(0, 0));
        SEntMan.System<FlammableSystem>().AdjustFireStacks(mob, stacks, ignite: true);
        return mob;
    }

    private int Hp(EntityUid mob)
    {
        return SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;
    }

    [Test]
    public async Task BurningTakesHitPoints()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid mob = default;
        var hp = 0;
        await Server.WaitPost(() => mob = Burning("CFStockHeatTestMob"));

        await Pair.RunSeconds(13);

        await Server.WaitPost(() => hp = Hp(mob));
        Assert.That(hp, Is.LessThan(100));
    }

    /// <summary>What used to happen: dead on the damage bar with every hit point left.</summary>
    [Test]
    public async Task FireKillsThroughHitPoints()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid mob = default;
        var hp = -1;
        var dead = false;
        await Server.WaitPost(() => mob = Burning("CFStockHeatTestFrail"));

        await Pair.RunSeconds(20);

        await Server.WaitPost(() =>
        {
            hp = Hp(mob);
            dead = SEntMan.System<MobStateSystem>().IsDead(mob);
        });
        Assert.That(dead);
        Assert.That(hp, Is.EqualTo(0));
    }

    [Test]
    public async Task FireResistanceCounts()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid mob = default;
        var hp = 0;
        await Server.WaitPost(() =>
        {
            mob = Burning("CFStockHeatTestMob");
            SEntMan.System<CFBonusSystem>().AddResistance(mob, "Fire", 9999, false, 60f);
        });

        await Pair.RunSeconds(13);

        await Server.WaitPost(() => hp = Hp(mob));
        Assert.That(hp, Is.EqualTo(100));
    }

    /// <summary>Only burning changes hands. Anything else stock does stays on the bar.</summary>
    [Test]
    public async Task OtherStockDamageIsLeftAlone()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var applied = false;
        var pending = true;
        await Server.WaitPost(() =>
        {
            var mob = SEntMan.SpawnEntity("CFStockHeatTestMob", _map.Local(0, 0));
            var damage = new DamageSpecifier();
            damage.DamageDict["Blunt"] = 5;
            applied = SEntMan.System<DamageableSystem>().TryChangeDamage(mob, damage);
            pending = SEntMan.HasComponent<CFStockHeatComponent>(mob);
        });

        Assert.That(applied, "It should still reach the damage bar.");
        Assert.That(pending, Is.False);
    }
}
