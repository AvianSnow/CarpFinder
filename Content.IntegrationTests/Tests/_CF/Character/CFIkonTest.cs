using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>An Exemplar's divine spark, ikons and transcendence, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFIkonSystem))]
public sealed class CFIkonTest : GameTest
{
    private static readonly Box2i Footprint = new(-2, -2, 4, 3);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFIkonHero
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
  parent: CFIkonHero
  id: CFIkonFoe
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    armorClass: 1
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFIkonSystem Ikons => SEntMan.System<CFIkonSystem>();

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private EntityUid Spawn(string id, int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Exemplar(params string[] ikons)
    {
        var hero = Spawn("CFIkonHero");
        foreach (var ikon in ikons)
        {
            Ikons.Add(hero, ikon);
        }

        return hero;
    }

    private EntityUid Action(EntityUid hero, string id)
    {
        foreach (var action in SEntMan.System<SharedActionsSystem>().GetActions(hero))
        {
            if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id)
                return action;
        }

        Assert.Fail($"{id} was not granted");
        return default;
    }

    private void Instant(EntityUid hero, EntityUid action)
    {
        var ev = new CFActivityInstantEvent { Performer = hero };
        SEntMan.EventBus.RaiseLocalEvent(action, ev);
    }

    private int Spark(EntityUid hero) => SEntMan.GetComponent<CFExemplarComponent>(hero).Spark;

    private int Hp(EntityUid mob) => SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    [Test]
    public async Task EveryIkonCanBeTakenInEverySlot()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Exemplar" && c.Group.Length > 0))
            {
                SEntMan.System<CFCharacterFeatureSystem>().Apply(Spawn("CFIkonHero"), choice.Grants, choice.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(3 * Server.ProtoMan.EnumeratePrototypes<CFIkonPrototype>().Count()));
    }

    [Test]
    public async Task TheSparkGivesTheBonusOfTheIkonItIsIn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Exemplar("ScarOfTheSurvivor", "ThousandLeagueSandals");
            Assert.That(Spark(hero), Is.EqualTo(0));
            Assert.That(Bonuses.Total(hero, CFBonusStat.Fortitude), Is.EqualTo(1));
            Assert.That(Bonuses.Total(hero, CFBonusStat.Speed), Is.EqualTo(0));

            Instant(hero, Action(hero, "CFActShiftThousandLeagueSandals"));
            Assert.That(Spark(hero), Is.EqualTo(1));
            Assert.That(Bonuses.Total(hero, CFBonusStat.Fortitude), Is.EqualTo(0));
            Assert.That(Bonuses.Total(hero, CFBonusStat.Speed), Is.EqualTo(10));

            // Shifting to where it already is sends it back to the soul.
            Instant(hero, Action(hero, "CFActShiftThousandLeagueSandals"));
            Assert.That(Spark(hero), Is.EqualTo(-1));
            Assert.That(Bonuses.Total(hero, CFBonusStat.Speed), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ATranscendenceHealsThenThrowsTheSparkOut()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Exemplar("ScarOfTheSurvivor", "ThousandLeagueSandals");
            var before = Hp(hero);
            Instant(hero, Action(hero, "CFActTransScarOfTheSurvivor"));
            Assert.That(Hp(hero), Is.GreaterThan(before));
            Assert.That(Spark(hero), Is.EqualTo(1));

            // Only the ikon holding the spark, once a round: the other transcendence is off for 6 seconds.
            var dash = Action(hero, "CFActTransThousandLeagueSandals");
            Assert.That(SEntMan.GetComponent<ActionComponent>(dash).Enabled, Is.False);
            Assert.That(SEntMan.GetComponent<ActionComponent>(Action(hero, "CFActTransScarOfTheSurvivor")).Enabled, Is.False);
        });
    }

    [Test]
    public async Task ATranscendenceWaitsForTheStrikeItNeeds()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitPost(() => hero = Exemplar("NobleBranch"));
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ActionComponent>(Action(hero, "CFActTransNobleBranch")).Enabled, Is.False);
        });
    }

    [Test]
    public async Task SplashHitsTheTargetAndItsNeighboursButNotTheExemplar()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Exemplar("HandsOfTheWildling");
            var foe = Spawn("CFIkonFoe", 1);
            var neighbour = Spawn("CFIkonFoe", 1);
            var before = Hp(hero);
            var strikes = SEntMan.System<CFStrikeSystem>();
            for (var i = 0; i < 100 && Hp(neighbour) == 500; i++)
            {
                strikes.PerformStrike(hero, foe, strikes.UnarmedStats(hero), hero, melee: true, countMap: false);
            }

            Assert.That(Hp(neighbour), Is.LessThan(500));
            Assert.That(Hp(hero), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task AnIkonOnlyAddsDamageToAWeaponThatFitsIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Exemplar("MortalHarvest");
            var strikes = SEntMan.System<CFStrikeSystem>();

            // Mortal Harvest needs an axe, flail or polearm, so fists add no persistent damage.
            var punched = Spawn("CFIkonFoe", 1);
            for (var i = 0; i < 40; i++)
            {
                strikes.PerformStrike(hero, punched, strikes.UnarmedStats(hero), hero, melee: true, countMap: false);
            }

            Assert.That(SEntMan.GetComponent<CFExemplarComponent>(hero).LastHit, Is.EqualTo(punched));
            Assert.That(SEntMan.HasComponent<CFPersistentDamageComponent>(punched), Is.False);

            var axe = SEntMan.SpawnEntity("CFBattleAxe", MapCoordinates.Nullspace);
            Assert.That(SEntMan.System<CFWeaponSystem>().TryStats(axe, out var stats), Is.True);
            var chopped = Spawn("CFIkonFoe", 1);
            for (var i = 0; i < 40 && !SEntMan.HasComponent<CFPersistentDamageComponent>(chopped); i++)
            {
                strikes.PerformStrike(hero, chopped, stats, axe, melee: true, countMap: false);
            }

            Assert.That(SEntMan.HasComponent<CFPersistentDamageComponent>(chopped), Is.True);
        });
    }

    [Test]
    public async Task AnAuraOnlyLastsWhileTheIkonHoldsTheSpark()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, ally = default;
        await Server.WaitPost(() =>
        {
            hero = Exemplar("VictorsWreath", "ScarOfTheSurvivor");
            ally = Spawn("CFIkonHero", 1);
        });
        await Pair.RunTicksSync(30);
        var during = 0;
        await Server.WaitPost(() =>
        {
            during = Bonuses.Total(ally, CFBonusStat.AttackRoll);
            Ikons.SetSpark(hero, 1);
        });
        await Pair.RunTicksSync(30);
        var after = 1;
        await Server.WaitPost(() => after = Bonuses.Total(ally, CFBonusStat.AttackRoll));

        Assert.Multiple(() =>
        {
            Assert.That(during, Is.EqualTo(1));
            Assert.That(after, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task AResistanceComesAndGoesWithTheSpark()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitAssertion(() =>
        {
            hero = Exemplar("SkinHardAsHornSlashing");
            Assert.That(Bonuses.ModifyDamage(hero, "Slashing", 10), Is.EqualTo(9));
            Ikons.SetSpark(hero, -1);
        });

        // It is kept topped up for a couple of seconds at a time, so it takes a moment to run out.
        await Pair.RunSeconds(4);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Bonuses.ModifyDamage(hero, "Slashing", 10), Is.EqualTo(10));
        });
    }
}
