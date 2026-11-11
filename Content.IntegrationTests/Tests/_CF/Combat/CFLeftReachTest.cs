using Robust.Shared.Prototypes;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>An enemy running out of reach sets off a Strike. Strength 40 means any Strike allowed to happen lands.</summary>
[TestFixture]
[TestOf(typeof(CFReactionSystem))]
public sealed class CFLeftReachTest : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    private static readonly Vector2 East = new(5f, 0f);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFLeftReachHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 12
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFLeftReachHero
  id: CFLeftReachFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFReactionSystem Reactions => SEntMan.System<CFReactionSystem>();

    private (EntityUid Hero, EntityUid Foe) Scene()
    {
        var hero = SEntMan.SpawnEntity("CFLeftReachHero", _map.Local(0, 0));
        var foe = SEntMan.SpawnEntity("CFLeftReachFoe", _map.Local(1, 0));
        SEntMan.GetComponent<CFCombatOptionsComponent>(hero).ReactiveStrike = true;
        return (hero, foe);
    }

    private void Push(EntityUid mob, Vector2 velocity)
    {
        var physics = SEntMan.System<SharedPhysicsSystem>();
        physics.WakeBody(mob);
        physics.SetLinearVelocity(mob, velocity);
    }

    private int Hurt(EntityUid mob)
    {
        return StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;
    }

    private int Spent(EntityUid mob)
    {
        return SEntMan.GetComponent<CFCombatOptionsComponent>(mob).ReactionSpent;
    }

    [Test]
    public async Task RunningAwayProvokesAStrikeThatIgnoresMap()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        await Server.WaitPost(() =>
        {
            (hero, foe) = Scene();
            Push(foe, East);
            Reactions.NoteLeftReach(hero, foe);
        });

        await Pair.RunTicksSync(2);

        int hurt = 0, spent = 0, map = -1;
        await Server.WaitPost(() =>
        {
            hurt = Hurt(foe);
            spent = Spent(hero);
            map = SEntMan.GetComponent<CFActionPoolComponent>(hero).MapAttacks;
        });

        Assert.Multiple(() =>
        {
            Assert.That(hurt, Is.GreaterThan(0));
            Assert.That(spent, Is.EqualTo(CFReactions.Strike));
            Assert.That(map, Is.Zero, "A reaction does not add to the multiple attack penalty.");
        });
    }

    private void Walk(EntityUid mob)
    {
        SEntMan.GetComponent<InputMoverComponent>(mob).HeldMoveButtons = MoveButtons.Walk;
        Push(mob, East);
    }

    private float Stamina(EntityUid mob)
    {
        return SEntMan.GetComponent<CFActionPoolComponent>(mob).Stamina;
    }

    [Test]
    public async Task WalkingOutIsAStepThatCostsAnAction()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        float before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            (hero, foe) = Scene();
            before = Stamina(foe);
            Walk(foe);
            Reactions.NoteLeftReach(hero, foe);
            after = Stamina(foe);
        });

        await Pair.RunTicksSync(2);

        var hurt = -1;
        var spent = -1;
        await Server.WaitPost(() =>
        {
            hurt = Hurt(foe);
            spent = Spent(hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.EqualTo(before - 1f));
            Assert.That(hurt, Is.Zero);
            Assert.That(spent, Is.Zero, "The hero keeps its reaction.");
        });
    }

    [Test]
    public async Task WalkingOutWithNoActionsLeftProvokes()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        await Server.WaitPost(() =>
        {
            (hero, foe) = Scene();
            SEntMan.GetComponent<CFActionPoolComponent>(foe).Stamina = 0f;
            Walk(foe);
            Reactions.NoteLeftReach(hero, foe);
        });

        await Pair.RunTicksSync(2);

        var hurt = 0;
        await Server.WaitPost(() => hurt = Hurt(foe));
        Assert.That(hurt, Is.GreaterThan(0));
    }

    [Test]
    public async Task OneStepClearsEveryoneLeftAtOnce()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        float before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            var (hero, foe) = Scene();
            var second = SEntMan.SpawnEntity("CFLeftReachHero", _map.Local(0, 0));
            SEntMan.GetComponent<CFCombatOptionsComponent>(second).ReactiveStrike = true;
            before = Stamina(foe);
            Walk(foe);
            Reactions.NoteLeftReach(hero, foe);
            Reactions.NoteLeftReach(second, foe);
            after = Stamina(foe);
        });

        Assert.That(after, Is.EqualTo(before - 1f));
    }

    [Test]
    public async Task AStepIsFreeWhenNobodyCouldHaveAnswered()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        float before = 0, afterSpent = 0, afterPlain = 0;
        await Server.WaitPost(() =>
        {
            var (hero, foe) = Scene();
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).ReactionSpent = CFReactions.Strike;
            before = Stamina(foe);
            Walk(foe);
            Reactions.NoteLeftReach(hero, foe);
            afterSpent = Stamina(foe);

            var plain = SEntMan.SpawnEntity("CFLeftReachHero", _map.Local(0, 0));
            Reactions.NoteLeftReach(plain, foe);
            afterPlain = Stamina(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(afterSpent, Is.EqualTo(before), "Its reaction was already used.");
            Assert.That(afterPlain, Is.EqualTo(before), "That one has no reaction to answer with.");
        });
    }

    [Test]
    public async Task AnEnemyThatCannotSeeYouDoesNotAnswer()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        await Server.WaitPost(() =>
        {
            (hero, foe) = Scene();
            var stealth = SEntMan.EnsureComponent<CFStealthComponent>(foe);
            stealth.Active = true;
            stealth.Detection[hero] = CFDetection.Hidden;
            Push(foe, East);
            Reactions.NoteLeftReach(hero, foe);
        });

        await Pair.RunTicksSync(2);

        var hurt = -1;
        await Server.WaitPost(() => hurt = Hurt(foe));
        Assert.That(hurt, Is.Zero);
    }

    [Test]
    public async Task LeavingAnEnemyBehindDoesNotCount()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        await Server.WaitPost(() =>
        {
            (hero, foe) = Scene();
            Push(hero, -East);
            Reactions.NoteLeftReach(hero, foe);
        });

        await Pair.RunTicksSync(2);

        var hurt = -1;
        await Server.WaitPost(() => hurt = Hurt(foe));
        Assert.That(hurt, Is.Zero);
    }

    [Test]
    public async Task BeingThrownOutOfReachDoesNotCount()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        await Server.WaitPost(() =>
        {
            (hero, foe) = Scene();
            SEntMan.EnsureComponent<ThrownItemComponent>(foe);
            Push(foe, East);
            Reactions.NoteLeftReach(hero, foe);
        });

        await Pair.RunTicksSync(2);

        var hurt = -1;
        await Server.WaitPost(() => hurt = Hurt(foe));
        Assert.That(hurt, Is.Zero);
    }

    [Test]
    public async Task OneReactionForTwoRunners()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, first = default, second = default;
        await Server.WaitPost(() =>
        {
            (hero, first) = Scene();
            second = SEntMan.SpawnEntity("CFLeftReachFoe", _map.Local(1, 0));
            Push(first, East);
            Push(second, East);
            Reactions.NoteLeftReach(hero, first);
            Reactions.NoteLeftReach(hero, second);
        });

        await Pair.RunTicksSync(2);

        int firstHurt = 0, secondHurt = -1;
        await Server.WaitPost(() =>
        {
            firstHurt = Hurt(first);
            secondHurt = Hurt(second);
        });

        Assert.Multiple(() =>
        {
            Assert.That(firstHurt, Is.GreaterThan(0));
            Assert.That(secondHurt, Is.Zero);
        });
    }

    [Test]
    public async Task StandStillIsTheSameTriggerFromAFeat()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid foe = default;
        await Server.WaitPost(() =>
        {
            var monk = SEntMan.SpawnEntity("CFLeftReachHero", _map.Local(0, 0));
            foe = SEntMan.SpawnEntity("CFLeftReachFoe", _map.Local(1, 0));
            SEntMan.System<CFCharacterFeatureSystem>().Apply(
                monk,
                Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFStandStill")).Grants,
                "feat:CFStandStill");
            Push(foe, East);
            Reactions.NoteLeftReach(monk, foe);
        });

        await Pair.RunTicksSync(2);

        var hurt = 0;
        await Server.WaitPost(() => hurt = Hurt(foe));
        Assert.That(hurt, Is.GreaterThan(0));
    }

    /// <summary>The whole path, with a real reach circle reporting a real exit.</summary>
    [Test]
    public async Task ARealRunOutOfTheReachCircleProvokes()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        await Server.WaitPost(() =>
        {
            (hero, foe) = Scene();
            var physics = SEntMan.System<SharedPhysicsSystem>();
            physics.WakeBody(hero);
            physics.WakeBody(foe);
        });

        await Pair.RunTicksSync(5);
        await Server.WaitPost(() => Push(foe, new Vector2(40f, 0f)));
        await Pair.RunTicksSync(30);

        var hurt = 0;
        var distance = 0f;
        await Server.WaitPost(() =>
        {
            hurt = Hurt(foe);
            var transform = SEntMan.System<SharedTransformSystem>();
            distance = (transform.GetWorldPosition(foe) - transform.GetWorldPosition(hero)).Length();
        });

        Assert.That(distance, Is.GreaterThan(CFFlanking.ReachInUnits(CFFlanking.DefaultReachFeet)), "It has to have left the circle for this to mean anything.");
        Assert.That(hurt, Is.GreaterThan(0));
    }
}
