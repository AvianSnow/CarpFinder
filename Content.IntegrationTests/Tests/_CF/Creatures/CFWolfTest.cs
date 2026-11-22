using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Encounters;
using Content.Shared.CF.Combat;
using Content.Shared.CF.GOAP;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>The wolf's Pack Attack, and the pack encounter. Damage tests use a test wolf with fixed numbers.</summary>
[TestFixture]
[TestOf(typeof(CFFlankingSystem))]
public sealed class CFWolfTest : GameTest
{
    private const int Strikes = 20;

    private static readonly Box2i Footprint = new(-3, -2, 3, 2);
    private static readonly Box2i SpawnerFootprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    // +30 against AC 1 always hits, and a 1d1 bite with no modifier deals 1, or 2 on a critical. So each Strike deals at
    // most 2 without Pack Attack, and at least 2 with it, at least 4 on a critical.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFWolfTestPrey
  components:
  - type: CFCombatant
    level: 1
    armorClass: 1
    maxHp: 10000
    currentHp: 10000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFMobWolf
  id: CFWolfTestPackmate
  components:
  - type: CFCombatant
    level: 1
    maxHp: 24
    currentHp: 24
    diesAtZeroHp: true
    printedAttack: 30
    printedDamage: 0
  - type: CFCombatOptions
    naturalDice: 1
    naturalDie: 1
    naturalType: Piercing
    packAttackDie: 4
";

    private CFDevMap _map = default!;

    /// <summary>Spawns prey with a biter beside it and the given allies, and returns the damage of all the bites.</summary>
    private int Bites(params (int X, int Y)[] allies)
    {
        var prey = SEntMan.SpawnEntity("CFWolfTestPrey", _map.Local(0, 0));
        var biter = SEntMan.SpawnEntity("CFWolfTestPackmate", _map.Local(1, 0));
        foreach (var (x, y) in allies)
        {
            SEntMan.SpawnEntity("CFWolfTestPackmate", _map.Local(x, y));
        }

        var strikes = SEntMan.System<CFStrikeSystem>();
        var defender = SEntMan.GetComponent<CFCombatantComponent>(prey);
        for (var i = 0; i < Strikes; i++)
        {
            strikes.PerformStrike(biter, prey, strikes.UnarmedStats(biter), biter, melee: true, countMap: false);
        }

        return 10000 - defender.CurrentHp;
    }

    [Test]
    public async Task OnlyAlliesWithThePreyInReachCount()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var flanking = SEntMan.System<CFFlankingSystem>();
            var prey = SEntMan.SpawnEntity("CFWolfTestPrey", _map.Local(0, 0));
            var biter = SEntMan.SpawnEntity("CFWolfTestPackmate", _map.Local(1, 0));
            Assert.That(flanking.AlliesThreatening(prey, biter), Is.Zero, "The biter itself does not count.");

            SEntMan.SpawnEntity("CFWolfTestPackmate", _map.Local(-1, 0));
            Assert.That(flanking.AlliesThreatening(prey, biter), Is.EqualTo(1));

            SEntMan.SpawnEntity("CFWolfTestPackmate", _map.Local(-3, 0));
            Assert.That(flanking.AlliesThreatening(prey, biter), Is.EqualTo(1), "A wolf 15 feet away has no reach.");

            SEntMan.SpawnEntity("CFWolfTestPackmate", _map.Local(0, 1));
            Assert.That(flanking.AlliesThreatening(prey, biter), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TwoPackmatesOnThePreyAddPackAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var damage = Bites((-1, 0), (0, 1));
            Assert.That(damage, Is.GreaterThan(2 * Strikes));
        });
    }

    [Test]
    public async Task OnePackmateIsNotEnough()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var damage = Bites((-1, 0));
            Assert.That(damage, Is.InRange(Strikes, 2 * Strikes));
        });
    }

    [Test]
    public async Task APackSpreadsOutToFlank()
    {
        _map = await CFDevMap.Load(Pair, SpawnerFootprint, avoidStructures: true);
        var members = 0;
        var flankers = 0;
        var beasts = 0;
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.SpawnEntity("CFEncounterSpawner", _map.Local(0, 0));
            var spawner = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(uid);
            spawner.Encounter = "WolfPack";

            SEntMan.System<CFGOAPEncounterSystem>().Spawn((uid, spawner));

            members = spawner.Members.Count;
            foreach (var member in spawner.Members)
            {
                var goap = SEntMan.GetComponent<CFGOAPComponent>(member);
                if (goap.Actions.Contains(new ProtoId<CFGOAPActionPrototype>("TakeFlank")))
                    flankers++;
                if (goap.Tactics.Id == "Beast")
                    beasts++;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(members, Is.EqualTo(4));
            Assert.That(flankers, Is.EqualTo(members), "Every wolf can take a flank.");
            Assert.That(beasts, Is.EqualTo(members), "The pack keeps the wolf's own tactics.");
        });
    }
}
