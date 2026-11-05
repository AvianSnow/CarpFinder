using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Hazards;
using Content.Shared.CF.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.Hazards;

/// <summary>
/// The spike trap prototype on the dev map (<c>Resources/Prototypes/Maps/debug.yml</c>):
/// noticing it, setting it off, faction allies, and disarming it.
/// Ability modifiers are extreme so a natural 1 or 20 cannot change the outcome.
/// </summary>
[TestFixture]
[TestOf(typeof(CFHazardSystem))]
public sealed class CFHazardTest : GameTest
{
    private const int StartHp = 200;

    private static readonly Box2i Footprint = new(-1, -1, 5, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFHazardTestHuman
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 200
  - type: CFAbilityScore
";

    private CFDevMap _map = default!;

    private EntityUid Human(int x, int y, int wisdom = 0, int dexterity = 0)
    {
        var mob = SEntMan.SpawnEntity("CFHazardTestHuman", _map.Local(x, y));
        var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(mob);
        abilities.AbilityModifiers[CFAbility.Wisdom] = wisdom;
        abilities.AbilityModifiers[CFAbility.Dexterity] = dexterity;
        SEntMan.GetComponent<CFCombatantComponent>(mob).PrintedPerception = wisdom;
        return mob;
    }

    private Entity<CFHazardComponent> Trap(string prototype = "CFSpikeTrap")
    {
        var trap = SEntMan.SpawnEntity(prototype, _map.Local(0, 0));
        return (trap, SEntMan.GetComponent<CFHazardComponent>(trap));
    }

    private int Hp(EntityUid uid) => SEntMan.GetComponent<CFCombatantComponent>(uid).CurrentHp;

    private void StepOnto(EntityUid mob, EntityUid trap)
    {
        SEntMan.System<SharedTransformSystem>().SetCoordinates(mob, SEntMan.GetComponent<TransformComponent>(trap).Coordinates);
        SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
    }

    [Test]
    public async Task CreaturesNearbyRollPerceptionOnce()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Entity<CFHazardComponent> trap = default;
        EntityUid sharp = default;
        EntityUid dull = default;

        await Server.WaitPost(() =>
        {
            trap = Trap();
            sharp = Human(3, 0, wisdom: 30);
            dull = Human(3, 1, wisdom: -30);
            var physics = SEntMan.System<SharedPhysicsSystem>();
            physics.WakeBody(sharp);
            physics.WakeBody(dull);
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(trap.Comp.Rolled, Does.Contain(sharp).And.Contain(dull), "Both are within 30 feet.");
            Assert.That(trap.Comp.DetectedBy, Does.Contain(sharp));
            Assert.That(trap.Comp.DetectedBy, Does.Not.Contain(dull));
            Assert.That(trap.Comp.Armed, "Noticing it does not set it off.");
            Assert.That(Hp(sharp), Is.EqualTo(StartHp));
            Assert.That(Hp(dull), Is.EqualTo(StartHp), "Failing to notice it does not set it off either.");
        });
    }

    [Test]
    public async Task SteppingOnItSetsItOff()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Entity<CFHazardComponent> trap = default;
        EntityUid victim = default;

        await Server.WaitPost(() =>
        {
            trap = Trap();
            victim = Human(3, 0, dexterity: -10);
        });

        await Pair.RunTicksSync(5);
        await Server.WaitPost(() => StepOnto(victim, trap));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(trap.Comp.Armed, Is.False);
            Assert.That(trap.Comp.Revealed, "A sprung trap is visible to everyone.");
            Assert.That(Hp(victim), Is.LessThan(StartHp), "2d8 piercing, DC 17 basic Reflex the victim cannot pass by 10.");
        });
    }

    [Test]
    public async Task AlliesOfTheTrapperAreSafe()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Entity<CFHazardComponent> trap = default;
        EntityUid kobold = default;

        await Server.WaitPost(() =>
        {
            trap = Trap("CFSpikeTrapMonster");
            kobold = Human(3, 0, dexterity: -10);
            SEntMan.EnsureComponent<CFFactionComponent>(kobold).Faction = "Monster";
        });

        await Pair.RunTicksSync(5);
        await Server.WaitPost(() => StepOnto(kobold, trap));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(trap.Comp.Armed);
            Assert.That(trap.Comp.Rolled, Does.Not.Contain(kobold), "Allies already know where it is.");
            Assert.That(Hp(kobold), Is.EqualTo(StartHp));
            Assert.That(SEntMan.System<CFHazardSystem>().CanSee(trap, kobold));
        });
    }

    /// <summary>
    /// The spike trap is a simple level 1 hazard. Against a level 1 party that is 8 XP each (GM Core),
    /// for everyone in the disarmer's company. A creature with no company gets it alone.
    /// </summary>
    [Test]
    public async Task DisarmingGivesTheCompanyHazardXp()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hazards = SEntMan.System<CFHazardSystem>();

            var rogue = Human(1, 0, dexterity: 30);
            var friend = Human(2, 0);
            var stranger = Human(3, 0);
            foreach (var member in new[] { rogue, friend })
            {
                SEntMan.EnsureComponent<CFFactionComponent>(member).Faction = "Alpha";
            }

            foreach (var mob in new[] { rogue, friend, stranger })
            {
                SEntMan.EnsureComponent<CFExperienceComponent>(mob);
            }

            var trap = Trap();
            Assert.That(trap.Comp.Level, Is.EqualTo(1));
            hazards.TryDisarm(trap, rogue, trap.Comp.Disarm[0]);

            Assert.That(SEntMan.GetComponent<CFExperienceComponent>(rogue).Xp, Is.EqualTo(8));
            Assert.That(SEntMan.GetComponent<CFExperienceComponent>(friend).Xp, Is.EqualTo(8), "The whole company shares it.");
            Assert.That(SEntMan.GetComponent<CFExperienceComponent>(stranger).Xp, Is.EqualTo(0));

            var lone = Trap();
            SEntMan.GetComponent<CFAbilityScoreComponent>(stranger).AbilityModifiers[CFAbility.Dexterity] = 30;
            hazards.TryDisarm(lone, stranger, lone.Comp.Disarm[0]);
            Assert.That(SEntMan.GetComponent<CFExperienceComponent>(stranger).Xp, Is.EqualTo(8), "No company: the disarmer alone.");
            Assert.That(SEntMan.GetComponent<CFExperienceComponent>(rogue).Xp, Is.EqualTo(8));
        });
    }

    [Test]
    public async Task DisarmSuccessRemovesItFailureSetsItOff()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hazards = SEntMan.System<CFHazardSystem>();
            var trap = Trap();
            var option = trap.Comp.Disarm[0];
            Assert.That(option.Skill, Is.EqualTo("Thievery"));
            Assert.That(option.Dc, Is.EqualTo(15));

            var expert = Human(1, 0, dexterity: 30);
            hazards.TryDisarm(trap, expert, option);
            Assert.That(SEntMan.IsQueuedForDeletion(trap) || SEntMan.Deleted(trap), "A disarmed trap is removed.");
            Assert.That(Hp(expert), Is.EqualTo(StartHp));

            var second = Trap();
            var clumsy = Human(1, 1, dexterity: -30);
            hazards.TryDisarm(second, clumsy, option);
            Assert.That(second.Comp.Armed, Is.False, "A failed attempt sets it off.");
            Assert.That(Hp(clumsy), Is.LessThan(StartHp));
        });
    }
}
