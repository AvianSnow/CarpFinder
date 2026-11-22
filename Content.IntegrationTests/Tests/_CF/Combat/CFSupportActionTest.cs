using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Medicine;
using Content.Shared.CF.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Medicine, Aid, Recall Knowledge, Reposition, Create a Diversion, and the shared entry points.</summary>
[TestFixture]
[TestOf(typeof(CFManeuverSystem))]
public sealed class CFSupportActionTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSupportHero
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

- type: entity
  parent: CFSupportHero
  id: CFSupportFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFManeuverSystem Maneuvers => SEntMan.System<CFManeuverSystem>();
    private CFMedicineSystem Medicine => SEntMan.System<CFMedicineSystem>();
    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    private EntityUid Hero(int x = 0, CFAbility? high = null, int value = Extreme)
    {
        var hero = SEntMan.SpawnEntity("CFSupportHero", _map.Local(x, 0));
        if (high is { } ability)
            SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[ability] = value;

        return hero;
    }

    private EntityUid Foe(int x)
    {
        return SEntMan.SpawnEntity("CFSupportFoe", _map.Local(x, 0));
    }

    private CFCombatantComponent Combat(EntityUid mob)
    {
        return SEntMan.GetComponent<CFCombatantComponent>(mob);
    }

    private float Stamina(EntityUid mob)
    {
        return SEntMan.GetComponent<CFActionPoolComponent>(mob).Stamina;
    }

    private EntityUid Healer()
    {
        var healer = Hero(high: CFAbility.Wisdom);
        Combat(healer).TrainedSkills.Add("Medicine");
        return healer;
    }

    [Test]
    public async Task TreatWoundsHealsAndClearsWounded()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var healer = Healer();
            var patient = Hero(1);
            Combat(patient).CurrentHp = 100;
            Combat(patient).Wounded = 2;

            Assert.That(Medicine.TreatWounds(healer, patient).IsSuccess());
            Assert.That(Combat(patient).CurrentHp, Is.InRange(102, 132), "2d8, or 4d8 on a critical success.");
            Assert.That(Combat(patient).Wounded, Is.Zero);
            Assert.That(SEntMan.GetComponent<CFPatientComponent>(patient).TreatWoundsUntil, Is.GreaterThan(TimeSpan.Zero));
        });
    }

    [Test]
    public async Task BattleMedicineCostsAnActionAndLeavesWounded()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var healer = Healer();
            var patient = Hero(1);
            Combat(patient).CurrentHp = 100;
            Combat(patient).Wounded = 2;

            Assert.That(Medicine.TryBattleMedicine(healer, patient));
            Assert.That(Combat(patient).CurrentHp, Is.GreaterThan(100));
            Assert.That(Combat(patient).Wounded, Is.EqualTo(2));
            Assert.That(Stamina(healer), Is.EqualTo(2f));

            Assert.That(Medicine.TryBattleMedicine(healer, patient), Is.False, "The patient is immune to this healer.");
            Assert.That(Stamina(healer), Is.EqualTo(2f));
        });
    }

    [Test]
    public async Task MedicineNeedsTraining()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var untrained = Hero(high: CFAbility.Wisdom);
            var patient = Hero(1);
            Combat(patient).CurrentHp = 100;

            Assert.That(Medicine.TryBattleMedicine(untrained, patient), Is.False);
            Assert.That(Stamina(untrained), Is.EqualTo(3f));
        });
    }

    [Test]
    public async Task BattleMedicineFindsTheHurtAlly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var healer = Healer();
            var enemy = Foe(1);
            Combat(enemy).CurrentHp = 1;
            Assert.That(Medicine.TryNearestPatient(healer, out _), Is.False, "Enemies are not patients, and no one else is hurt.");

            var patient = Hero(1);
            Combat(patient).CurrentHp = 100;
            Assert.That(Medicine.TryNearestPatient(healer, out var found));
            Assert.That(found, Is.EqualTo(patient));
        });
    }

    [Test]
    public async Task AidWaitsForTheNextCheck()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var helper = Hero(high: CFAbility.Wisdom);
            var ally = Hero(1);
            Combat(helper).PrintedPerception = Extreme;

            Assert.That(Maneuvers.TryAid(helper, ally));
            Assert.That(Stamina(helper), Is.EqualTo(2f));
            Assert.That(Conditions.TakeAid(ally), Is.InRange(1, 2));
            Assert.That(Conditions.TakeAid(ally), Is.Zero, "It only helps once.");
        });
    }

    [Test]
    public async Task RecallKnowledgeGetsOneTry()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(high: CFAbility.Intelligence);
            var foe = Foe(1);

            Assert.That(Maneuvers.TryRecall(hero, foe)?.IsSuccess(), Is.True);
            Assert.That(Stamina(hero), Is.EqualTo(2f));

            Assert.That(Maneuvers.TryRecall(hero, foe), Is.Null);
            Assert.That(Stamina(hero), Is.EqualTo(2f));
        });
    }

    [Test]
    public async Task RepositionRollsAthletics()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var strong = Hero(high: CFAbility.Strength);
            var weak = Hero(high: CFAbility.Strength, value: -Extreme);
            var foe = Foe(1);

            Assert.That(Maneuvers.TryReposition(strong, foe));
            Assert.That(Stamina(strong), Is.EqualTo(2f));
            Assert.That(Maneuvers.TryReposition(weak, foe), Is.False);
            Assert.That(Stamina(weak), Is.EqualTo(2f));
        });
    }

    [Test]
    public async Task ADiversionHidesFromThoseItFools()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var stealth = SEntMan.System<CFStealthSystem>();
            var hero = Hero(high: CFAbility.Charisma);
            var foe = Foe(2);
            var sharp = Foe(3);
            Combat(sharp).PrintedPerception = 200;

            Assert.That(stealth.TryDiversion(hero));
            Assert.That(stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Hidden));
            Assert.That(stealth.Detection(hero, sharp), Is.EqualTo(CFDetection.Observed));
            Assert.That(Stamina(hero), Is.EqualTo(2f));
        });
    }

    [Test]
    public async Task EntryPointsFollowTheSameRules()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(high: CFAbility.Charisma);
            var foe = Foe(1);
            var ally = Hero(1);

            Assert.That(Maneuvers.CanUse(hero, CFBasicAction.Feint, foe), Is.False, "Feint needs training.");
            Combat(hero).TrainedSkills.Add("Deception");
            Assert.That(Maneuvers.CanUse(hero, CFBasicAction.Feint, foe));
            Assert.That(Maneuvers.CanUse(hero, CFBasicAction.Feint, ally), Is.False, "Allies are not fooled.");
            Assert.That(Maneuvers.CanUse(hero, CFBasicAction.Sneak), Is.False, "Sneak needs to be hiding first.");
            Assert.That(Maneuvers.CanUse(hero, CFBasicAction.Aid, ally));

            Assert.That(Maneuvers.TryUse(hero, CFBasicAction.Feint, foe));
            Assert.That(Conditions.IsOffGuardTo(foe, hero));
            Assert.That(Stamina(hero), Is.EqualTo(2f));

            SEntMan.GetComponent<CFActionPoolComponent>(hero).Stamina = 0f;
            Assert.That(Maneuvers.CanUse(hero, CFBasicAction.Aid, ally), Is.False, "No actions left.");
            Assert.That(Maneuvers.TryUse(hero, CFBasicAction.Aid, ally), Is.False);
        });
    }
}
