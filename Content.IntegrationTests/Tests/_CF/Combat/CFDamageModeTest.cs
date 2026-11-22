using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Switching a weapon's damage type, on the hand cannon.</summary>
[TestFixture]
[TestOf(typeof(CFDamageModeSystem))]
public sealed class CFDamageModeTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFDamageModeHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
";

    private CFDevMap _map = default!;

    private EntityUid SpawnHere(string id) => SEntMan.SpawnEntity(id, _map.Local(0, 0));

    private void Use(EntityUid hero, EntityUid gun)
    {
        SEntMan.EventBus.RaiseLocalEvent(gun, new UseInHandEvent(hero));
    }

    private string Type(EntityUid gun)
    {
        SEntMan.System<CFWeaponSystem>().TryStats(gun, out var stats);
        return stats.DamageType;
    }

    [Test]
    public async Task UsingInHandCyclesTheDamageType()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string first = "", second = "", third = "", wrapped = "";
        await Server.WaitPost(() =>
        {
            var hero = SpawnHere("CFDamageModeHero");
            var gun = SpawnHere("CFHandCannon");
            first = Type(gun);
            Use(hero, gun);
            second = Type(gun);
            Use(hero, gun);
            third = Type(gun);
            Use(hero, gun);
            wrapped = Type(gun);
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("Bludgeoning"));
            Assert.That(second, Is.EqualTo("Piercing"));
            Assert.That(third, Is.EqualTo("Slashing"));
            Assert.That(wrapped, Is.EqualTo("Bludgeoning"));
        });
    }

    [Test]
    public async Task SwitchingCostsAnAction()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        float spent = 0;
        var stayed = "";
        await Server.WaitPost(() =>
        {
            var hero = SpawnHere("CFDamageModeHero");
            var gun = SpawnHere("CFHandCannon");
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            var before = pool.Stamina;
            Use(hero, gun);
            spent = before - pool.Stamina;

            pool.Stamina = 0;
            var type = Type(gun);
            Use(hero, gun);
            stayed = Type(gun) == type ? "same" : "changed";
        });

        Assert.Multiple(() =>
        {
            Assert.That(spent, Is.EqualTo(1f));
            Assert.That(stayed, Is.EqualTo("same"), "With no action left, it does not switch.");
        });
    }
}
