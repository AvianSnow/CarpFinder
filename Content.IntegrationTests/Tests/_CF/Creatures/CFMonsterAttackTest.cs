using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>
/// What monsters share: several Strikes with their own numbers, Grab and Knockdown after a hit, resistance to all
/// damage, fast healing, conditions they have by nature, and elite and weak. On test creatures with fixed numbers,
/// because real monsters get rebalanced.
/// </summary>
[TestFixture]
[TestOf(typeof(CFMonsterAttackSystem))]
public sealed class CFMonsterAttackTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFMonsterAttackTestBeast
  components:
  - type: CFCombatant
    level: 1
    armorClass: 15
    maxHp: 200
    currentHp: 100
    printedAttack: 3
    printedDamage: 1
    printedSkills: { Athletics: 60 }
    thinkingTimer: 0
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Monster
  - type: CFMonsterAttacks
    attacks:
    - name: jaws
      attack: 60
      dice: 2
      die: 8
      bonus: 4
      damageType: Piercing
      followUp: Knockdown
    - name: claw
      attack: 60
      die: 6
      bonus: 2
      damageType: Slashing
      traits: [ Agile ]
    - name: tail
      attack: 50
      die: 8
      damageType: Bludgeoning
      traits: [ Reach ]
  - type: CFResistances
    entries:
    - all: true
      except: [ Force ]
      amount: 5
  - type: CFFastHealing
    perPulse: 7
    deactivatedBy: [ Fire ]
  - type: CFInnateConditions
    conditions:
      Slowed: 1

- type: entity
  parent: MobHuman
  id: CFMonsterAttackTestPrey
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 200
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private CFMonsterAttackSystem Attacks => SEntMan.System<CFMonsterAttackSystem>();
    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    private EntityUid Spawn(string id, int x)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    [Test]
    public async Task TheMainAttackComesFirstAndAnAgileOneAfter()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var beast = Spawn("CFMonsterAttackTestBeast", 0);
            var comp = SEntMan.GetComponent<CFMonsterAttacksComponent>(beast);
            var strikes = SEntMan.System<CFStrikeSystem>();

            Assert.That(Attacks.Best((beast, comp)), Is.Zero, "With no penalty the first listed attack wins a tie.");
            var jaws = strikes.UnarmedStats(beast);
            Assert.Multiple(() =>
            {
                Assert.That(jaws.DamageDie, Is.EqualTo(8));
                Assert.That(jaws.PrintedAttack, Is.EqualTo(60), "The attack's own bonus, not the creature's printedAttack.");
                Assert.That(jaws.PrintedDamage, Is.EqualTo(4));
            });

            SEntMan.System<CFActionPoolSystem>().CountAttack(beast);
            Assert.That(Attacks.Best((beast, comp)), Is.EqualTo(1), "With a penalty, the agile claw is the better roll.");
            Attacks.Select((beast, comp), 1);
            var claw = strikes.UnarmedStats(beast);
            Assert.That(claw.Agile && claw.DamageDie == 6 && claw.DamageType == "Slashing");
        });
    }

    [Test]
    public async Task AnAttackWithReachIsUsedOnWhatTheOthersCannotGetTo()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var beast = Spawn("CFMonsterAttackTestBeast", 0);
            var near = Spawn("CFMonsterAttackTestPrey", 1);
            var far = Spawn("CFMonsterAttackTestPrey", 2);
            var comp = SEntMan.GetComponent<CFMonsterAttacksComponent>(beast);
            var melee = SEntMan.GetComponent<Content.Shared.Weapons.Melee.MeleeWeaponComponent>(beast);
            var shortRange = melee.Range;

            Assert.That(Attacks.ReachFeet(comp.Attacks[2]), Is.EqualTo(10), "The Reach trait is 10 feet.");
            Assert.That(Attacks.Best((beast, comp), near), Is.Zero, "Beside it, the jaws are the better attack.");
            Assert.That(Attacks.Best((beast, comp), far), Is.EqualTo(2), "Ten feet away only the tail reaches.");

            Attacks.PickFor(beast, far);
            Assert.That(comp.Current, Is.EqualTo(2));
            Assert.That(melee.Range, Is.GreaterThan(shortRange), "The body swings as far as the tail reaches.");

            Attacks.PickFor(beast, near);
            Assert.That(comp.Current, Is.Zero);
            Assert.That(melee.Range, Is.EqualTo(shortRange).Within(0.01f), "And no farther with the jaws.");
        });
    }

    [Test]
    public async Task AHitWithTheJawsKnocksDownAndAGrabHolds()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var beast = Spawn("CFMonsterAttackTestBeast", 0);
            var prey = Spawn("CFMonsterAttackTestPrey", 1);
            var strikes = SEntMan.System<CFStrikeSystem>();
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(beast);
            var before = pool.Stamina;

            // +60 against AC 10 always hits, and Athletics +60 always trips.
            strikes.PerformStrike(beast, prey, strikes.UnarmedStats(beast), beast, melee: true);
            Assert.That(Conditions.IsProne(prey), "Knockdown follows the jaws.");
            Assert.That(pool.Stamina, Is.EqualTo(before - 1f), "The follow-up costs an action. The test Strike itself was not paid for.");

            var other = Spawn("CFMonsterAttackTestPrey", 2);
            Assert.That(SEntMan.System<CFManeuverSystem>().FollowUp(beast, other, CFFollowUp.Grab, free: true));
            Assert.That(SEntMan.GetComponent<CFConditionComponent>(other).Grabbed);
        });
    }

    [Test]
    public async Task ItResistsAllButForceAndIsSlowedByNature()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var beast = Spawn("CFMonsterAttackTestBeast", 0);
            var bonuses = SEntMan.System<CFBonusSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(bonuses.ModifyDamage(beast, "Fire", 8), Is.EqualTo(3));
                Assert.That(bonuses.ModifyDamage(beast, "Slashing", 8), Is.EqualTo(3));
                Assert.That(bonuses.ModifyDamage(beast, "Force", 8), Is.EqualTo(8), "Force is the exception.");
                Assert.That(SEntMan.GetComponent<CFActionPoolComponent>(beast).MaxActions, Is.EqualTo(2), "Slowed 1 by nature.");
                Assert.That(Conditions.Value(beast, CFConditionKind.Slowed), Is.EqualTo(1));
            });
        });
    }

    [Test]
    public async Task FastHealingMendsUntilTheRightDamageStopsIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var beast = default(EntityUid);
        await Server.WaitPost(() => beast = Spawn("CFMonsterAttackTestBeast", 0));

        // Seven seconds: one pulse of 7.
        await Pair.RunTicksSync(210);
        await Server.WaitAssertion(() =>
        {
            var combat = SEntMan.GetComponent<CFCombatantComponent>(beast);
            Assert.That(combat.CurrentHp, Is.GreaterThanOrEqualTo(107));

            SEntMan.System<CFEffectSystem>().DealDamage(beast, beast, 20, "Fire", critical: false);
            var healing = SEntMan.GetComponent<CFFastHealingComponent>(beast);
            Assert.That(healing.StoppedUntil, Is.GreaterThan(Server.Timing.CurTime), "Fire stops it for a while.");
        });
    }

    [Test]
    public async Task AnEliteCreatureIsTwoBetterAndALevelHigher()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var beast = Spawn("CFMonsterAttackTestBeast", 0);
            SEntMan.System<CFAdjustmentSystem>().Adjust(beast, CFAdjustmentKind.Elite);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(beast);
            var attacks = SEntMan.GetComponent<CFMonsterAttacksComponent>(beast);
            Assert.Multiple(() =>
            {
                Assert.That(combat.ArmorClass, Is.EqualTo(17));
                Assert.That(combat.Level, Is.EqualTo(2));
                Assert.That(combat.MaxHp, Is.EqualTo(210), "An elite of level 1 has 10 more Hit Points.");
                Assert.That(attacks.Attacks[0].Attack, Is.EqualTo(62));
                Assert.That(attacks.Attacks[0].Bonus, Is.EqualTo(6));
                Assert.That(combat.PrintedSkills["Athletics"], Is.EqualTo(62));
            });

            SEntMan.System<CFAdjustmentSystem>().Adjust(beast, CFAdjustmentKind.Elite);
            Assert.That(combat.ArmorClass, Is.EqualTo(17), "A creature is adjusted once.");
            Assert.That(CFAdjustmentSystem.HitPoints(3, elite: false), Is.EqualTo(-15));
        });
    }
}
