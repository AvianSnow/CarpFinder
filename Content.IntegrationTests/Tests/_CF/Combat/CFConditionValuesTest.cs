using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Enfeebled, drained, doomed, fatigued, deafened, fascinated and stunned, and the fatigue a Rage can leave.</summary>
[TestFixture]
[TestOf(typeof(CFConditionSystem))]
public sealed class CFConditionValuesTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFConditionValuesHero
  components:
  - type: CFCombatant
    level: 3
    armorClass: 15
    maxHp: 30
    currentHp: 30
  - type: CFAbilityScore
  - type: CFCombatOptions
    canRage: true
    secondWind: true
  - type: CFActionPool
";

    private CFDevMap _map = default!;

    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    private EntityUid Hero() => SEntMan.SpawnEntity("CFConditionValuesHero", _map.Local(0, 0));

    private void Give(EntityUid hero, CFConditionKind kind, int value = 1, float seconds = 60f)
    {
        Conditions.ApplyCondition(hero, kind, value, seconds, hero, 0);
    }

    [Test]
    public async Task EachConditionLowersWhatItShould()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int acBefore = 0, ac = 0, fortBefore = 0, fort = 0, perceptionBefore = 0, perception = 0, skill = 0;
        int deathBefore = 0, death = 0, enfeebled = 0, ended = -1;
        float stunned = 0f;
        await Server.WaitPost(() =>
        {
            var dying = SEntMan.System<CFDyingSystem>();
            var hero = Hero();
            acBefore = Conditions.ArmorClassModifier(hero);
            fortBefore = Conditions.DefenseModifier(hero, CFDefenseType.Fortitude);
            perceptionBefore = Conditions.DefenseModifier(hero, CFDefenseType.Perception);
            deathBefore = dying.DeathValue(hero);

            Give(hero, CFConditionKind.Fatigued);
            Give(hero, CFConditionKind.Drained, 2);
            Give(hero, CFConditionKind.Deafened);
            Give(hero, CFConditionKind.Fascinated);
            Give(hero, CFConditionKind.Doomed, 2);
            Give(hero, CFConditionKind.Enfeebled, 2);

            ac = Conditions.ArmorClassModifier(hero);
            fort = Conditions.DefenseModifier(hero, CFDefenseType.Fortitude);
            perception = Conditions.DefenseModifier(hero, CFDefenseType.Perception);
            skill = Conditions.SkillBonus(hero, "Diplomacy");
            death = dying.DeathValue(hero);
            enfeebled = Conditions.Value(hero, CFConditionKind.Enfeebled);

            Conditions.EndCondition(hero, CFConditionKind.Enfeebled);
            ended = Conditions.Value(hero, CFConditionKind.Enfeebled);

            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            SEntMan.System<CFActionPoolSystem>().Ensure(hero);
            var before = pool.Stamina;
            Give(hero, CFConditionKind.Stunned, 2, 0f);
            stunned = before - pool.Stamina;
        });

        Assert.Multiple(() =>
        {
            Assert.That(ac - acBefore, Is.EqualTo(-1), "Fatigued: -1 AC.");
            Assert.That(fort - fortBefore, Is.EqualTo(-2), "Drained 2 on Fortitude, and fatigue does not stack with it.");
            Assert.That(perception - perceptionBefore, Is.EqualTo(-2), "Deafened and fascinated do not stack.");
            Assert.That(skill, Is.EqualTo(-2), "Fascinated: -2 to skill checks.");
            Assert.That(death - deathBefore, Is.EqualTo(-2), "Doomed 2 dies two steps sooner.");
            Assert.That(enfeebled, Is.EqualTo(2));
            Assert.That(ended, Is.Zero);
            Assert.That(stunned, Is.EqualTo(2f), "Stunned 2 is two actions gone.");
        });
    }

    [Test]
    public async Task AConditionRunsOut()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        var during = 0;
        await Server.WaitPost(() =>
        {
            hero = Hero();
            Give(hero, CFConditionKind.Enfeebled, 1, 1f);
            during = Conditions.Value(hero, CFConditionKind.Enfeebled);
        });
        await Pair.RunSeconds(2f);

        var after = -1;
        await Server.WaitPost(() => after = Conditions.Value(hero, CFConditionKind.Enfeebled));

        Assert.Multiple(() =>
        {
            Assert.That(during, Is.EqualTo(1));
            Assert.That(after, Is.Zero);
        });
    }

    [Test]
    public async Task SecondWindLeavesTheBarbarianFatigued()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int afterFirst = -1, afterSecond = -1, rounds = 0;
        await Server.WaitPost(() =>
        {
            var activities = SEntMan.System<CFActivitySystem>();
            var hero = Hero();

            // The first Rage takes its temporary Hit Points on time, so nothing is owed.
            activities.SetRaging(hero, true);
            rounds = activities.RageRoundsLeft(hero);
            activities.SetRaging(hero, false);
            afterFirst = Conditions.Value(hero, CFConditionKind.Fatigued);

            // The second comes within the minute, and Second Wind takes them anyway.
            activities.SetRaging(hero, true);
            activities.SetRaging(hero, false);
            afterSecond = Conditions.Value(hero, CFConditionKind.Fatigued);
        });

        Assert.Multiple(() =>
        {
            Assert.That(rounds, Is.EqualTo(10), "A minute of Rage is 10 rounds.");
            Assert.That(afterFirst, Is.Zero);
            Assert.That(afterSecond, Is.EqualTo(1));
        });
    }

    /// <summary>A fight begins with the first attack after a quiet spell, which is what stands in for initiative.</summary>
    [Test]
    public async Task AFightBeginsWithTheFirstAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool raged = false, fighting = false, surprised = false, readyFoe = true;
        await Server.WaitPost(() =>
        {
            var fights = SEntMan.System<CFFightSystem>();
            var barbarian = Hero();
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(barbarian);
            options.QuickTempered = true;
            var attacker = Hero();
            fights.NoteAttack(attacker, barbarian);
            raged = options.Raging;
            fighting = fights.IsFighting(barbarian);

            var rogue = Hero();
            SEntMan.GetComponent<CFCombatOptionsComponent>(rogue).SurpriseAttack = true;
            var quiet = Hero();
            fights.NoteAttack(rogue, quiet);
            surprised = Conditions.IsOffGuardTo(quiet, rogue);

            // The barbarian is already in a fight, so it has acted.
            var second = Hero();
            SEntMan.GetComponent<CFCombatOptionsComponent>(second).SurpriseAttack = true;
            fights.NoteAttack(second, barbarian);
            readyFoe = Conditions.IsOffGuardTo(barbarian, second);
        });

        Assert.Multiple(() =>
        {
            Assert.That(raged, Is.True, "Quick-Tempered rages as the fight begins.");
            Assert.That(fighting, Is.True);
            Assert.That(surprised, Is.True, "Surprise Attack: a creature that has not acted is off-guard.");
            Assert.That(readyFoe, Is.False, "Not one that is already fighting.");
        });
    }

    [Test]
    public async Task HuntPreyWithMonsterHunterRecallsKnowledgeForFree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool couldBefore = false, canAfter = true;
        float spent = -1f;
        await Server.WaitPost(() =>
        {
            var ranger = Hero();
            var prey = Hero();
            SEntMan.GetComponent<CFCombatOptionsComponent>(ranger).MonsterHunter = true;
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(ranger);
            SEntMan.System<CFActionPoolSystem>().Ensure(ranger);
            var before = pool.Stamina;
            couldBefore = Conditions.CanRecall(ranger, prey);

            SEntMan.System<CFActivitySystem>().Mark(ranger, prey, prey: true);
            spent = before - pool.Stamina;
            canAfter = Conditions.CanRecall(ranger, prey);
        });

        Assert.Multiple(() =>
        {
            Assert.That(couldBefore, Is.True);
            Assert.That(canAfter, Is.False, "The one try at this creature was made with the Hunt Prey.");
            Assert.That(spent, Is.Zero, "It cost no action of its own.");
        });
    }
}
