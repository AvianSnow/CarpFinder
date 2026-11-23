using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Investigator: Intelligence on a stratagem's Strike, Shared Stratagem, Person of Interest and Skillful Lessons.</summary>
[TestFixture]
[TestOf(typeof(CFStrikeSystem))]
public sealed class CFInvestigatorTest : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<CFClassPrototype> Investigator = "Investigator";

    public override PoolSettings PoolSettings => new() { Connected = false };

    // The hero cannot hit AC 30 with Strength or Dexterity, and cannot miss it with Intelligence.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFInvestigatorTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Intelligence: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFInvestigatorTestHero
  id: CFInvestigatorTestFoe
  components:
  - type: CFCombatant
    level: 1
    armorClass: 30
    maxHp: 5000
    currentHp: 5000
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private EntityUid Spawn(string id = "CFInvestigatorTestHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    /// <summary>Stores a middling stratagem roll against a fresh foe and Strikes it. Returns the damage dealt and the foe.</summary>
    private (int Hurt, EntityUid Foe) StratagemStrike(EntityUid hero, string weaponId)
    {
        var strikes = SEntMan.System<CFStrikeSystem>();
        var foe = Spawn("CFInvestigatorTestFoe", 1);
        var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
        options.StratagemRoll = 10;
        options.StratagemTarget = foe;
        var item = Spawn(weaponId);
        SEntMan.System<CFWeaponSystem>().TryStats(item, out var stats);
        strikes.PerformStrike(hero, foe, stats, item, melee: true, countMap: false, fixedMap: 0);
        return (StartHp - SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp, foe);
    }

    [Test]
    public async Task AStratagemUsesIntelligenceWithAFinesseWeaponOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        int dagger = 0, longsword = 0;
        bool shared = false, notToSelf = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var ally = Spawn(x: 2);
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).SharedStratagem = true;
            longsword = StratagemStrike(hero, "CFLongsword").Hurt;
            var (hurt, foe) = StratagemStrike(hero, "CFDagger");
            dagger = hurt;

            var conditions = SEntMan.System<CFConditionSystem>();
            shared = conditions.IsOffGuardTo(foe, ally, true);
            notToSelf = conditions.IsOffGuardTo(foe, hero, true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(longsword, Is.EqualTo(0), "A longsword is neither agile nor finesse: no Intelligence, and a miss");
            Assert.That(dagger, Is.GreaterThan(0), "A dagger's stratagem adds Intelligence, and hits");
            Assert.That(shared, Is.True, "Shared Stratagem: off-guard to the ally");
            Assert.That(notToSelf, Is.False);
        });
    }

    [Test]
    public async Task PersonOfInterestMakesTheStratagemFree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        float before = 0, afterFree = 0, afterPaid = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(hero);
            options.PointMark = CFPointMark.Stratagem;
            var suspect = Spawn("CFInvestigatorTestFoe", 1);
            var stranger = Spawn("CFInvestigatorTestFoe", 2);
            SEntMan.System<CFEffectSystem>().Run(new List<CFEffect> { new CFPersonOfInterestEffect() }, new CFEffectArgs
            {
                User = hero,
                Source = hero,
                Target = suspect,
            });

            var maneuvers = SEntMan.System<CFManeuverSystem>();
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            before = pool.Stamina;
            maneuvers.TryMark((hero, options), suspect);
            afterFree = pool.Stamina;
            maneuvers.TryMark((hero, options), stranger);
            afterPaid = pool.Stamina;
        });

        Assert.Multiple(() =>
        {
            Assert.That(afterFree, Is.EqualTo(before), "Free against the person of interest");
            Assert.That(afterPaid, Is.EqualTo(before - 1f), "One action against anyone else");
        });
    }

    [Test]
    public async Task SkillfulLessonsGivesSkillFeatSlots()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.ProtoMan.Index(Investigator).BonusSkillFeats, Is.EquivalentTo(new[] { 3, 5 }));
        });
    }
}
