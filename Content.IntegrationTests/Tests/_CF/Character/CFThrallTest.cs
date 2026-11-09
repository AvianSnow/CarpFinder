using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Companions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>A Necromancer's thralls and grave spells, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFThrallServerSystem))]
public sealed class CFThrallTest : GameTest
{
    private static readonly Box2i Footprint = new(-2, -2, 4, 3);
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFThrallHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 400
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFThrallHero
  id: CFThrallFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private EntityUid Spawn(string id, int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Necromancer(params string[] choices)
    {
        var hero = Spawn("CFThrallHero");
        var feature = Server.ProtoMan.Index<CFClassPrototype>("Necromancer").Features.First(f => f.Name == "Grave Spells");
        Features.Apply(hero, feature.Grants, "grave");
        foreach (var id in choices)
        {
            Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);
        }

        return hero;
    }

    private void Raise(EntityUid hero, int count)
    {
        var ev = new CFCreateThrallsEvent(count);
        SEntMan.EventBus.RaiseLocalEvent(hero, ref ev);
    }

    private int CountThralls(EntityUid hero)
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<CFThrallComponent>();
        while (query.MoveNext(out _, out var thrall))
        {
            if (thrall.Owner == hero)
                count++;
        }

        return count;
    }

    private EntityUid FirstThrall(EntityUid hero)
    {
        var query = SEntMan.EntityQueryEnumerator<CFThrallComponent>();
        while (query.MoveNext(out var uid, out var thrall))
        {
            if (thrall.Owner == hero)
                return uid;
        }

        return default;
    }

    private int Hurt(EntityUid mob) => StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    [Test]
    public async Task CreateThrallMakesThemAndAPuppeteerMakesMore()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, puppeteer = 0;
        await Server.WaitPost(() =>
        {
            var hero = Necromancer();
            Raise(hero, 2);
            plain = CountThralls(hero);

            var other = Necromancer("MethodPuppeteer");
            Raise(other, 2);
            puppeteer = CountThralls(other);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(2));
            Assert.That(puppeteer, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task AThrallFallsApartAfterItsMinute()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        var after = 1;
        await Server.WaitPost(() =>
        {
            hero = Necromancer();
            Raise(hero, 1);
            SEntMan.GetComponent<CFThrallComponent>(FirstThrall(hero)).Expires = Server.ResolveDependency<IGameTiming>().CurTime;
        });
        await Pair.RunTicksSync(3);
        await Server.WaitPost(() => after = CountThralls(hero));

        Assert.That(after, Is.EqualTo(0));
    }

    [Test]
    public async Task ThrallsFailAndAreAlwaysHit()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Necromancer();
            Raise(hero, 1);
            var thrall = FirstThrall(hero);
            var degree = SEntMan.System<CFConditionSystem>().RollDefense(thrall, CFDefenseType.Fortitude, 1, out _);
            Assert.That(degree, Is.EqualTo(CFDegree.Failure));

            var combat = SEntMan.GetComponent<CFCombatantComponent>(thrall);
            Assert.That(combat.MaxHp, Is.EqualTo(1));

            // An armor class nothing could reach.
            combat.ArmorClass = 500;
            var foe = Spawn("CFThrallFoe", 1);
            var strikes = SEntMan.System<CFStrikeSystem>();
            strikes.PerformStrike(foe, thrall, strikes.UnarmedStats(foe), foe, melee: true, countMap: false);
            Assert.That(combat.CurrentHp, Is.LessThanOrEqualTo(0));
        });
    }

    [Test]
    public async Task AThrallDoesNotFollowItsOwner()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Necromancer();
            Raise(hero, 1);
            var thrall = FirstThrall(hero);
            var companions = SEntMan.System<CFCompanionSystem>();
            companions.ComeAlong(thrall);
            Assert.That(SEntMan.GetComponent<CFCompanionComponent>(thrall).Following, Is.False);
        });
    }

    [Test]
    public async Task NecroticBombHurtsAroundTheThrallAndUsesIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, far = default;
        var near = new EntityUid[6];
        await Server.WaitPost(() =>
        {
            hero = Necromancer();
            Raise(hero, 1);
            var thrall = FirstThrall(hero);
            for (var i = 0; i < near.Length; i++)
            {
                near[i] = Spawn("CFThrallFoe", 1);
            }

            far = SEntMan.SpawnEntity("CFThrallFoe", _map.Local(4, 2));
            var effects = SEntMan.System<CFEffectSystem>();
            var bomb = SEntMan.SpawnEntity("CFNecroticBomb", MapCoordinates.Nullspace);
            effects.ResolveTarget(bomb, hero, thrall);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(CountThralls(hero), Is.EqualTo(0));
            Assert.That(Hurt(far), Is.EqualTo(0));

            // Any one of them can save, but not all six.
            Assert.That(near.Sum(Hurt), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ACommandSendsTheNearestThrallAndNeedsOne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<CFThrallServerSystem>();
            var hero = Necromancer();
            var foe = Spawn("CFThrallFoe", 1);
            Assert.That(system.Command(hero, foe, 0), Is.False);

            Raise(hero, 2);
            Assert.That(system.Command(hero, foe, 1), Is.True);

            var query = SEntMan.EntityQueryEnumerator<CFThrallComponent, CFCompanionComponent>();
            var ordered = 0;
            while (query.MoveNext(out _, out var thrall, out var companion))
            {
                if (thrall.Owner == hero && companion.Target == foe)
                {
                    ordered++;
                    Assert.That(thrall.ChargeDice, Is.EqualTo(1));
                }
            }

            Assert.That(ordered, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BloodThrallsReturnHitPoints()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        var before = 0;
        await Server.WaitPost(() =>
        {
            hero = Necromancer("FascinationBlood");
            before = SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
            Raise(hero, 1);
            SEntMan.DeleteEntity(FirstThrall(hero));
        });
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public async Task EveryNecromancerOptionCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Necromancer"))
            {
                Features.Apply(Spawn("CFThrallHero"), choice.Grants, choice.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(6));
    }
}
