using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>What each Champion cause does when damage is about to land, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFReactionSystem))]
public sealed class CFChampionCauseTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);
    private const int FarAway = 7;
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCauseHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFCauseHero
  id: CFCauseFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFReactionSystem Reactions => SEntMan.System<CFReactionSystem>();

    private EntityUid Spawn(string id = "CFCauseHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    private EntityUid Champion(string cause)
    {
        var hero = Spawn();
        var features = SEntMan.System<CFCharacterFeatureSystem>();
        var aura = Server.ProtoMan.Index<CFClassPrototype>("Champion").Features.First(f => f.Name == "Champion's Aura");
        features.Apply(hero, aura.Grants, "feature:aura");
        features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(cause).Grants, $"choice:{cause}");
        return hero;
    }

    private int Hurt(EntityUid mob) => StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    [Test]
    public async Task JusticeProtectsOncePerReaction()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int first = 0, second = 0;
        await Server.WaitPost(() =>
        {
            Champion("CauseJustice");
            var ally = Spawn(x: 1);
            var foe = Spawn("CFCauseFoe", 1);
            first = Reactions.AnswerDamage(ally, foe, 20);
            second = Reactions.AnswerDamage(ally, foe, 20);
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(17), "2 + level 1 comes off.");
            Assert.That(second, Is.EqualTo(20), "The reaction is spent.");
        });
    }

    [Test]
    public async Task BothHaveToBeInTheAura()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int allyFar = 0, foeFar = 0;
        await Server.WaitPost(() =>
        {
            Champion("CauseJustice");
            allyFar = Reactions.AnswerDamage(Spawn(x: FarAway), Spawn("CFCauseFoe", 1), 20);
            foeFar = Reactions.AnswerDamage(Spawn(x: 1), Spawn("CFCauseFoe", FarAway), 20);
        });

        Assert.Multiple(() =>
        {
            Assert.That(allyFar, Is.EqualTo(20));
            Assert.That(foeFar, Is.EqualTo(20));
        });
    }

    /// <summary>The whole path: damage dealt the normal way is reduced before it reaches the hit points.</summary>
    [Test]
    public async Task RealDamageIsReduced()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hurt = 0;
        await Server.WaitPost(() =>
        {
            Champion("CauseGrandeur");
            var ally = Spawn(x: 1);
            SEntMan.System<CFEffectSystem>().DealDamage(Spawn("CFCauseFoe", 1), ally, 20, "Slashing", critical: false);
            hurt = Hurt(ally);
        });

        Assert.That(hurt, Is.EqualTo(17));
    }

    [Test]
    public async Task DesecrationResistsAndMarks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var left = 0;
        EntityUid? marked = null, foe = null;
        await Server.WaitPost(() =>
        {
            var hero = Champion("CauseDesecration");
            foe = Spawn("CFCauseFoe", 1);
            left = Reactions.AnswerDamage(hero, foe.Value, 20);
            marked = SEntMan.GetComponent<CFCombatOptionsComponent>(hero).MarkedTarget;
        });

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.EqualTo(18), "2 + half of level 1, rounded down.");
            Assert.That(marked, Is.EqualTo(foe));
        });
    }

    [Test]
    public async Task IniquityHurtsBoth()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int left = 0, dealt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Champion("CauseIniquity");
            var foe = Spawn("CFCauseFoe", 1);
            left = Reactions.AnswerDamage(hero, foe, 20);
            dealt = Hurt(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.InRange(21, 26));
            Assert.That(dealt, Is.InRange(1, 6));
        });
    }

    [Test]
    public async Task ObedienceHurtsTheEnemy()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var dealt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Champion("CauseObedience");
            var foe = Spawn("CFCauseFoe", 1);
            Reactions.AnswerDamage(hero, foe, 20);
            dealt = Hurt(foe);
        });

        Assert.That(dealt, Is.InRange(1, 6));
    }

    [Test]
    public async Task RedemptionWeakensTheEnemy()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int attack = 0, damage = 0;
        await Server.WaitPost(() =>
        {
            Champion("CauseRedemption");
            var foe = Spawn("CFCauseFoe", 1);
            Reactions.AnswerDamage(Spawn(x: 1), foe, 20);
            var bonuses = SEntMan.System<CFBonusSystem>();
            attack = bonuses.Total(foe, CFBonusStat.AttackRoll);
            damage = bonuses.Total(foe, CFBonusStat.Damage);
        });

        Assert.Multiple(() =>
        {
            Assert.That(attack, Is.EqualTo(-2));
            Assert.That(damage, Is.EqualTo(-2));
        });
    }

    /// <summary>Strength 40 makes the Escape certain, so one damaging hit is enough to see it.</summary>
    [Test]
    public async Task LiberationFreesAGrabbedAlly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool before = false, after = true;
        float spent = 1, full = 0;
        await Server.WaitPost(() =>
        {
            Champion("CauseLiberation");
            var ally = Spawn(x: 1);
            var foe = Spawn("CFCauseFoe", 1);
            SEntMan.GetComponent<CFAbilityScoreComponent>(ally).AbilityModifiers[CFAbility.Strength] = 40;
            var conditions = SEntMan.System<CFConditionSystem>();
            conditions.ApplyGrabbed(ally, foe);
            var condition = SEntMan.GetComponent<CFConditionComponent>(ally);
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(ally);
            full = pool.Stamina;
            before = condition.Grabbed;
            Reactions.AnswerDamage(ally, foe, 20);
            after = condition.Grabbed;
            spent = full - pool.Stamina;
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.True);
            Assert.That(after, Is.False);
            Assert.That(spent, Is.EqualTo(0f), "The Escape is free.");
        });
    }
}
