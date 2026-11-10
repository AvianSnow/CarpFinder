using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Temporary Hit Points: what damage does to them, how they stack and run out, and the ones from Rage.</summary>
[TestFixture]
[TestOf(typeof(CFTemporaryHpSystem))]
public sealed class CFTemporaryHpTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFTempHpTestHero
  components:
  - type: CFCombatant
    level: 3
    maxHp: 30
    currentHp: 30
  - type: CFAbilityScore
    abilityModifiers:
      Constitution: 2
  - type: CFCombatOptions
    canRage: true
";

    private CFDevMap _map = default!;

    private CFTemporaryHpSystem TempHp => SEntMan.System<CFTemporaryHpSystem>();

    private EntityUid Hero() => SEntMan.SpawnEntity("CFTempHpTestHero", _map.Local(0, 0));

    private CFCombatantComponent Combat(EntityUid uid) => SEntMan.GetComponent<CFCombatantComponent>(uid);

    [Test]
    public async Task DamageTakesThemFirstAndALowerGrantDoesNotStack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int temp = -1, hp = 0, kept = 0;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            TempHp.Grant(hero, 5, 60f);
            SEntMan.System<CFEffectSystem>().DealDamage(Hero(), hero, 8, "Bludgeoning", critical: false);
            temp = Combat(hero).TemporaryHp;
            hp = Combat(hero).CurrentHp;

            var other = Hero();
            TempHp.Grant(other, 10, 60f);
            TempHp.Grant(other, 4, 60f);
            kept = Combat(other).TemporaryHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(temp, Is.Zero);
            Assert.That(hp, Is.EqualTo(27), "5 of the 8 came off the temporary Hit Points.");
            Assert.That(kept, Is.EqualTo(10), "They do not stack, and the higher stays.");
        });
    }

    [Test]
    public async Task TheyRunOut()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = Hero();
            TempHp.Grant(hero, 5, 1f);
            Combat(hero).TemporaryHpUntil = SGameTiming.CurTime;
        });

        await RunSeconds(0.7f);

        var left = -1;
        await Server.WaitPost(() => left = Combat(hero).TemporaryHp);
        Assert.That(left, Is.Zero);
    }

    [Test]
    public async Task RageGivesThemOnceAMinuteUnlessSecondWind()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int first = 0, afterEnd = -1, again = -1, secondWind = 0;
        await Server.WaitPost(() =>
        {
            var activities = SEntMan.System<CFActivitySystem>();
            var hero = Hero();
            activities.SetRaging(hero, true);
            first = Combat(hero).TemporaryHp;
            activities.SetRaging(hero, false);
            afterEnd = Combat(hero).TemporaryHp;
            activities.SetRaging(hero, true);
            again = Combat(hero).TemporaryHp;

            activities.SetRaging(hero, false);
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).SecondWind = true;
            activities.SetRaging(hero, true);
            secondWind = Combat(hero).TemporaryHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(3 + 2), "Level plus Constitution.");
            Assert.That(afterEnd, Is.Zero, "They end with the Rage.");
            Assert.That(again, Is.Zero, "Not again within a minute.");
            Assert.That(secondWind, Is.EqualTo(5));
        });
    }
}
