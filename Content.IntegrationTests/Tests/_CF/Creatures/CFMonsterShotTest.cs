using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>
/// A monster's ranged attack as an action, and one that deals no damage: a web that sticks what it hits until it
/// Escapes. On a test web with fixed numbers, because the real spider gets rebalanced.
/// </summary>
[TestFixture]
[TestOf(typeof(CFMonsterShotSystem))]
public sealed class CFMonsterShotTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 7, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    // An attack of +60 against AC 10 always hits. The test web's Escape DC is out of reach, because a creature with no
    // player tries to Escape by itself at once, and the test lowers it when it wants the prey out.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: CFActMonsterShotBase
  id: CFMonsterShotTestWeb
  components:
  - type: CFTargeting
    rangeFeet: 30
  - type: CFProjectileDelivery
    boltPrototype: CFWebBolt
    speed: 30
  - type: CFCheck
    fixedDc: 70
  - type: CFDegreeEffects
    landed:
    - !type:CFStickEffect
      holder: CFWebTrap
      escapeDc: 70

- type: entity
  parent: MobHuman
  id: CFMonsterShotTestSpinner
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 200
    thinkingTimer: 0
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Monster
  - type: ActionGrant
    actions:
    - CFMonsterShotTestWeb

- type: entity
  parent: MobHuman
  id: CFMonsterShotTestPrey
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

    private CFMonsterShotSystem Shots => SEntMan.System<CFMonsterShotSystem>();
    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    [Test]
    public async Task AWebSticksWhatItHitsUntilItEscapes()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var spinner = default(EntityUid);
        var prey = default(EntityUid);
        await Server.WaitAssertion(() =>
        {
            spinner = SEntMan.SpawnEntity("CFMonsterShotTestSpinner", _map.Local(0, 0));
            prey = SEntMan.SpawnEntity("CFMonsterShotTestPrey", _map.Local(4, 0));
        });

        // The action is granted as the creature appears.
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Shots.TryGetReady(spinner, out var shot), "The monster has its ranged attack.");
            Assert.That(Shots.Worthwhile(spinner, shot, prey));
            Assert.That(Shots.TryShoot(spinner, shot, prey));
            Assert.That(SEntMan.GetComponent<CFActionPoolComponent>(spinner).Stamina, Is.EqualTo(2f), "The web is 1 action.");
        });

        // The web flies, then lands.
        await Pair.RunTicksSync(60);
        var web = default(EntityUid);
        await Server.WaitAssertion(() =>
        {
            var cond = SEntMan.GetComponent<CFConditionComponent>(prey);
            Assert.That(cond.Grabbed, "The prey is stuck.");
            Assert.That(cond.Grabber, Is.Not.Null);
            web = cond.Grabber!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<CFStickyHoldComponent>(web), "A web holds it, not the spider.");
                Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(prey).CurrentWalkSpeed, Is.Zero);
                Assert.That(Conditions.IsOffGuardTo(prey, spinner), Is.False, "Stuck is immobilized, not off-guard.");
                Assert.That(SEntMan.GetComponent<CFCombatantComponent>(prey).CurrentHp, Is.EqualTo(200), "The web deals no damage.");
                Assert.That(Shots.TryGetReady(spinner, out var again) && Shots.Worthwhile(spinner, again, prey), Is.False,
                    "The AI does not web what is already stuck.");
            });

            // Against the web's own DC, not anyone's Athletics: this low, even a natural 1 gets out.
            SEntMan.GetComponent<CFStickyHoldComponent>(web).EscapeDc = -20;
            Assert.That(SEntMan.System<CFManeuverSystem>().TryEscape((prey, cond), free: true), "It Escapes against the web's DC.");
            Assert.That(cond.Grabbed, Is.False);
            Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(prey).CurrentWalkSpeed, Is.GreaterThan(0f));
        });

        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(web), "The torn web is gone."));
    }

    [Test]
    public async Task TheAiKeepsItsWebForPreyOutOfReach()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var spinner = default(EntityUid);
        var near = default(EntityUid);
        var far = default(EntityUid);
        await Server.WaitAssertion(() =>
        {
            spinner = SEntMan.SpawnEntity("CFMonsterShotTestSpinner", _map.Local(0, 0));
            near = SEntMan.SpawnEntity("CFMonsterShotTestPrey", _map.Local(1, 0));
            far = SEntMan.SpawnEntity("CFMonsterShotTestPrey", _map.Local(7, 1));
        });

        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Shots.TryGetReady(spinner, out var shot));
            Assert.That(Shots.Worthwhile(spinner, shot, near), Is.False, "Within reach its fangs are better.");
            Assert.That(Shots.Worthwhile(spinner, shot, far), Is.False, "Thirty-five feet is past the web's 30.");
        });
    }
}
