using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.CombatMode;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Skill and class actions done with stock inputs: screaming, pointing, and playing an instrument.</summary>
[TestFixture]
[TestOf(typeof(CFManeuverSystem))]
public sealed class CFSocialActionTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSocialHero
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
  parent: CFSocialHero
  id: CFSocialFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFManeuverSystem Maneuvers => SEntMan.System<CFManeuverSystem>();
    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();
    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private EntityUid Hero(bool combatMode = true)
    {
        var hero = SEntMan.SpawnEntity("CFSocialHero", _map.Local(0, 0));
        SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Charisma] = Extreme;
        SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(hero, combatMode);
        Aim(hero, new Vector2(1f, 0f));
        return hero;
    }

    private void Aim(EntityUid hero, Vector2 direction)
    {
        SEntMan.System<SharedTransformSystem>().SetWorldRotation(hero, direction.ToWorldAngle());
    }

    private EntityUid Foe(int x)
    {
        return SEntMan.SpawnEntity("CFSocialFoe", _map.Local(x, 0));
    }

    private float Stamina(EntityUid mob)
    {
        return SEntMan.GetComponent<CFActionPoolComponent>(mob).Stamina;
    }

    private void TrainDeception(EntityUid hero)
    {
        SEntMan.GetComponent<CFCombatantComponent>(hero).TrainedSkills.Add("Deception");
    }

    private Entity<CFCombatOptionsComponent> Options(EntityUid mob)
    {
        return (mob, SEntMan.GetComponent<CFCombatOptionsComponent>(mob));
    }

    [Test]
    public async Task ScreamFrightensTheEnemyYouAimAt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe(3);

            Aim(hero, new Vector2(-1f, 0f));
            Assert.That(Maneuvers.TryAimedDemoralize(hero), Is.False, "Facing away finds no one.");
            Assert.That(Stamina(hero), Is.EqualTo(3f), "An ordinary scream costs nothing.");

            Aim(hero, new Vector2(1f, 0f));
            Assert.That(Maneuvers.TryAimedDemoralize(hero));
            Assert.That(Conditions.Frightened(foe), Is.GreaterThan(0));
            Assert.That(Stamina(hero), Is.EqualTo(2f));

            Assert.That(Maneuvers.TryAimedDemoralize(hero), Is.False, "A demoralized enemy is skipped for a minute.");
            Assert.That(Stamina(hero), Is.EqualTo(2f));
        });
    }

    [Test]
    public async Task ScreamNeedsCombatModeAndRange()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var calm = Hero(combatMode: false);
            Foe(3);
            Assert.That(Maneuvers.TryAimedDemoralize(calm), Is.False);
            Assert.That(Stamina(calm), Is.EqualTo(3f));
        });
    }

    [Test]
    public async Task PointingInReachFeints()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            TrainDeception(hero);
            var foe = Foe(1);
            var far = Foe(5);

            Maneuvers.PointAt(Options(hero), far);
            Assert.That(Stamina(hero), Is.EqualTo(3f), "Out of reach it stays a gesture.");

            Maneuvers.PointAt(Options(hero), foe);
            Assert.That(Stamina(hero), Is.EqualTo(2f));
            Assert.That(Conditions.IsOffGuardTo(foe, hero));
        });
    }

    [Test]
    public async Task FeintNeedsCombatModeAndTraining()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var untrained = Hero();
            var foe = Foe(1);
            Maneuvers.PointAt(Options(untrained), foe);
            Assert.That(Stamina(untrained), Is.EqualTo(3f), "Feint needs trained Deception.");

            var calm = Hero(combatMode: false);
            TrainDeception(calm);
            Maneuvers.PointAt(Options(calm), foe);
            Assert.That(Stamina(calm), Is.EqualTo(3f));
        });
    }

    [Test]
    public async Task FirstPointMarksAndSecondFeints()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            TrainDeception(hero);
            var foe = Foe(1);
            var options = Options(hero);
            options.Comp.PointMark = CFPointMark.Prey;

            Maneuvers.PointAt(options, foe);
            Assert.That(options.Comp.Prey, Is.EqualTo(foe));
            Assert.That(Conditions.IsOffGuardTo(foe, hero), Is.False);

            Maneuvers.PointAt(options, foe);
            Assert.That(Conditions.IsOffGuardTo(foe, hero));
            Assert.That(Stamina(hero), Is.EqualTo(1f));
        });
    }

    [Test]
    public async Task AFinisherNeedsPanacheAndAnEnemyInReach()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe(3);
            var options = Options(hero).Comp;
            options.PanacheStyle = true;

            Assert.That(Maneuvers.TryFinisher(hero), Is.False, "No panache.");

            options.Panache = true;
            Assert.That(Maneuvers.TryFinisher(hero), Is.False, "Out of reach.");
            Assert.That(Stamina(hero), Is.EqualTo(3f));

            SEntMan.System<SharedTransformSystem>().SetCoordinates(foe, _map.Local(1, 0));
            Assert.That(Maneuvers.TryFinisher(hero));
            Assert.That(options.Panache, Is.False);
            Assert.That(Stamina(hero), Is.EqualTo(2f));
        });
    }

    [Test]
    public async Task PlayingAnInstrumentPerformsTheAnthem()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var anthem = SEntMan.System<CFAnthemSystem>();
            var bard = Hero();
            var ally = SEntMan.SpawnEntity("CFSocialHero", _map.Local(2, 0));
            var foe = Foe(3);

            Assert.That(anthem.TryPlay(bard), Is.False, "Only a Bard knows it.");

            Options(bard).Comp.Anthem = true;
            Assert.That(anthem.TryPlay(bard));
            Assert.That(Stamina(bard), Is.EqualTo(2f));
            foreach (var mob in new[] { bard, ally })
            {
                Assert.That(Bonuses.Total(mob, CFBonusStat.AttackRoll), Is.EqualTo(1));
                Assert.That(Bonuses.Total(mob, CFBonusStat.MeleeDamage), Is.EqualTo(1));
            }

            Assert.That(Bonuses.Total(foe, CFBonusStat.AttackRoll), Is.Zero);
            Assert.That(anthem.TryPlay(bard), Is.False, "It only repeats once the last one runs out.");
            Assert.That(Stamina(bard), Is.EqualTo(2f));
        });
    }
}
