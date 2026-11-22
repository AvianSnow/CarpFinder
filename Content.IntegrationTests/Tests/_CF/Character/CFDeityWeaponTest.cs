using System;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>What a Cleric gets from the deity's favored weapon: Deadly Simplicity and Emblazon Armament.</summary>
[TestFixture]
[TestOf(typeof(CFEmblazonSystem))]
public sealed class CFDeityWeaponTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);
    private const int Swings = 400;

    public override PoolSettings PoolSettings => new() { Connected = false };

    // The deity is a test one, so the pantheon can change. Its favored weapon is a test spear with fixed numbers.
    [TestPrototypes]
    private const string Prototypes = @"
- type: cfDeity
  id: CFTestDeitySpear
  name: cf-deity-solhara
  description: cf-deity-solhara-desc
  grants:
    - !type:CFGrantWeaponFamiliarity
      weapons: [ CFDeityTestSpear ]
      favored: true

- type: cfDeity
  id: CFTestDeityOther
  name: cf-deity-keth
  description: cf-deity-keth-desc

- type: entity
  parent: CFSpear
  id: CFDeityTestSpear
  components:
  - type: CFWeapon
    damageDice: 1
    damageDie: 6
    category: Simple

- type: entity
  parent: CFDagger
  id: CFDeityTestDagger
  components:
  - type: CFWeapon
    damageDice: 1
    damageDie: 4
    category: Simple

- type: entity
  parent: MobHuman
  id: CFDeityTestHero
  components:
  - type: CFCombatant
    thinkingTimer: 0
    level: 1
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFDeityTestHero
  id: CFDeityTestFoe
  components:
  - type: CFCombatant
    armorClass: 8
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private EntityUid Cleric(string deity, params string[] feats)
    {
        var hero = SEntMan.SpawnEntity("CFDeityTestHero", _map.Local(0, 0));
        var build = new CFCharacterBuildData { Class = "Cleric", Deity = deity };
        SEntMan.EnsureComponent<CFExperienceComponent>(hero).Build = build;
        var features = SEntMan.System<CFCharacterFeatureSystem>();
        features.Apply(hero, Server.ProtoMan.Index<CFDeityPrototype>(deity).Grants, $"deity:{deity}");
        foreach (var feat in feats)
        {
            features.Apply(hero, Server.ProtoMan.Index<CFFeatPrototype>(feat).Grants, $"feat:{feat}");
        }

        return hero;
    }

    /// <summary>The least and most damage of plain hits. Critical hits are left out, because they double.</summary>
    private (int Least, int Most) Hits(EntityUid hero, EntityUid weapon)
    {
        var foe = SEntMan.SpawnEntity("CFDeityTestFoe", _map.Local(1, 0));
        var strikes = SEntMan.System<CFStrikeSystem>();
        SEntMan.System<CFWeaponSystem>().TryStats(weapon, out var stats);
        int least = int.MaxValue, most = 0;
        for (var i = 0; i < Swings; i++)
        {
            strikes.PerformStrike(hero, foe, stats, weapon, melee: true, countMap: false, fixedMap: 0, resolved: (degree, damage) =>
            {
                if (degree != CFDegree.Success)
                    return;

                least = Math.Min(least, damage);
                most = Math.Max(most, damage);
            });
        }

        return (least, most);
    }

    [Test]
    public async Task DeadlySimplicityRaisesTheFavoredWeaponsDie()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        (int Least, int Most) plain = default, deadly = default, otherWeapon = default;
        await Server.WaitPost(() =>
        {
            var spear = SEntMan.SpawnEntity("CFDeityTestSpear", _map.Local(0, 0));
            var dagger = SEntMan.SpawnEntity("CFDeityTestDagger", _map.Local(0, 0));
            plain = Hits(Cleric("CFTestDeitySpear"), spear);
            var hero = Cleric("CFTestDeitySpear", "CFDeadlySimplicity");
            deadly = Hits(hero, spear);
            otherWeapon = Hits(hero, dagger);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain.Most, Is.EqualTo(6), "A d6 spear.");
            Assert.That(deadly.Most, Is.EqualTo(8), "One step larger.");
            Assert.That(otherWeapon.Most, Is.EqualTo(4), "Only the favored weapon.");
        });
    }

    [Test]
    public async Task AnEmblazonedWeaponHitsHarderForAFollower()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        (int Least, int Most) follower = default, stranger = default;
        bool marked = false, noDeity = true;
        var symbols = 0;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            var emblazon = SEntMan.System<CFEmblazonSystem>();
            var spear = SEntMan.SpawnEntity("CFDeityTestSpear", _map.Local(0, 0));
            var second = SEntMan.SpawnEntity("CFDeityTestSpear", _map.Local(0, 0));
            hero = Cleric("CFTestDeitySpear");

            noDeity = emblazon.TryEmblazon(SEntMan.SpawnEntity("CFDeityTestHero", _map.Local(0, 0)), spear);
            marked = emblazon.TryEmblazon(hero, spear);
            follower = Hits(hero, spear);
            stranger = Hits(Cleric("CFTestDeityOther"), spear);

            emblazon.TryEmblazon(hero, second);
        });
        await Pair.RunSeconds(0.2f);
        await Server.WaitPost(() =>
        {
            var query = SEntMan.AllEntityQueryEnumerator<CFEmblazonedComponent>();
            while (query.MoveNext(out _, out var mark))
            {
                // Only this hero's: the pair is reused, so another test's symbol may still be about.
                if (mark.Emblazoner == hero)
                    symbols++;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(noDeity, Is.False, "It needs a deity.");
            Assert.That(marked, Is.True);
            Assert.That((follower.Least, follower.Most), Is.EqualTo((2, 7)), "1d6 and the symbol's 1.");
            Assert.That((stranger.Least, stranger.Most), Is.EqualTo((1, 6)), "Another deity's follower gets nothing.");
            Assert.That(symbols, Is.EqualTo(1), "The first symbol fades when the second is drawn.");
        });
    }

    [Test]
    public async Task AnEmblazonedShieldBlocksOneMore()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, marked = 0;
        await Server.WaitPost(() =>
        {
            var hero = Cleric("CFTestDeitySpear");
            var shield = SEntMan.SpawnEntity("CFSteelShield", _map.Local(0, 0));
            var comp = SEntMan.GetComponent<CFShieldComponent>(shield);
            var shields = SEntMan.System<CFShieldSystem>();
            var hit = comp.Hardness + 3;
            plain = shields.Block(hero, (shield, comp), hit);
            SEntMan.System<CFEmblazonSystem>().TryEmblazon(hero, shield);
            marked = shields.Block(hero, (shield, comp), hit);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(3), "A plain shield takes its Hardness off.");
            Assert.That(marked, Is.EqualTo(2), "The symbol adds 1 to it.");
        });
    }
}
