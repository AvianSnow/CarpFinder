using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Locks;
using Content.Shared.CF.Preferences;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Lock;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Locks;

/// <summary>
/// Keys, Pick a Lock and Force Open on the dev map.
/// Ability modifiers are extreme so that the degree of success is fixed: a natural 1 or 20 moves one step at most.
/// </summary>
[TestFixture]
[TestOf(typeof(CFLockSystem))]
public sealed class CFLockTest : GameTest
{
    private const int Strong = 60;
    private const int Tries = 20;

    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFLockTestHuman
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
";

    private CFDevMap _map = default!;

    private CFLockSystem Locks => SEntMan.System<CFLockSystem>();
    private LockSystem StockLocks => SEntMan.System<LockSystem>();

    private EntityUid Human(int dexterity = 0, int strength = 0, bool trained = true)
    {
        var mob = SEntMan.SpawnEntity("CFLockTestHuman", _map.Local(1, 0));
        var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(mob);
        abilities.AbilityModifiers[CFAbility.Dexterity] = dexterity;
        abilities.AbilityModifiers[CFAbility.Strength] = strength;
        if (trained)
            SEntMan.GetComponent<CFCombatantComponent>(mob).TrainedSkills.Add("Thievery");
        return mob;
    }

    private Entity<CFLockComponent> Door()
    {
        var door = SEntMan.SpawnEntity("CFWoodDoorLocked", _map.Local(0, 0));
        return (door, SEntMan.GetComponent<CFLockComponent>(door));
    }

    private EntityUid Give(EntityUid mob, string item)
    {
        var uid = SEntMan.SpawnEntity(item, _map.Local(1, 0));
        Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(mob, uid, checkActionBlocker: false));
        return uid;
    }

    private void Rest(EntityUid mob)
    {
        if (SEntMan.TryGetComponent(mob, out CFActionPoolComponent? pool))
        {
            pool.Stamina = 100f;
            pool.MapAttacks = 0;
        }
    }

    [Test]
    public async Task LockedDoorStaysShutAndStockTogglesAreRefused()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var door = Door();
            var mob = Human();

            Assert.That(StockLocks.IsLocked(door.Owner));
            Assert.That(SEntMan.System<SharedDoorSystem>().CanOpen(door), Is.False);
            Assert.That(StockLocks.TryUnlock(door, mob), Is.False);
            Assert.That(StockLocks.IsLocked(door.Owner));
        });
    }

    [Test]
    public async Task KeyOpensItsLockAndOnlyItsLock()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var door = Door();
            var mob = Human();
            var key = SEntMan.GetComponent<CFKeyComponent>(SEntMan.SpawnEntity("CFKeyIron", _map.Local(1, 0)));

            var wrong = new CFKeyComponent { KeyId = "Other" };
            Assert.That(Locks.TryUseKey(door, mob, wrong), Is.False);
            Assert.That(StockLocks.IsLocked(door.Owner));

            Assert.That(Locks.TryUseKey(door, mob, key));
            Assert.That(StockLocks.IsLocked(door.Owner), Is.False);
            Assert.That(SEntMan.System<SharedDoorSystem>().CanOpen(door));

            Assert.That(Locks.TryUseKey(door, mob, key));
            Assert.That(StockLocks.IsLocked(door.Owner));
        });
    }

    [Test]
    public async Task KeyWillNotLockAnOpenDoor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var door = Door();
            var mob = Human();
            var key = SEntMan.GetComponent<CFKeyComponent>(SEntMan.SpawnEntity("CFKeyIron", _map.Local(1, 0)));

            Assert.That(Locks.TryUseKey(door, mob, key));
            SEntMan.System<SharedDoorSystem>().SetState(door, DoorState.Open);

            Assert.That(Locks.TryUseKey(door, mob, key), Is.False);
            Assert.That(StockLocks.IsLocked(door.Owner), Is.False);
        });
    }

    [Test]
    public async Task PickingNeedsTrainingAndToolkit()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var door = Door();
            var untrained = Human(dexterity: Strong, trained: false);
            Give(untrained, "CFThievesToolkit");
            var noTools = Human(dexterity: Strong);

            Assert.That(Locks.TryPick(door, untrained), Is.False);
            Assert.That(Locks.TryPick(door, noTools), Is.False);
            Assert.That(door.Comp.Successes, Is.Zero);
        });
    }

    [Test]
    public async Task CriticalSuccessesCountDoubleUntilTheLockOpens()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var door = Door();
            var mob = Human(dexterity: Strong);
            Give(mob, "CFThievesToolkit");

            Assert.That(Locks.TryPick(door, mob), Is.False);
            Assert.That(door.Comp.Successes, Is.GreaterThanOrEqualTo(1));
            Assert.That(StockLocks.IsLocked(door.Owner));

            Rest(mob);
            Assert.That(Locks.TryPick(door, mob) || Locks.TryPick(door, mob));
            Assert.That(StockLocks.IsLocked(door.Owner), Is.False);
            Assert.That(door.Comp.Successes, Is.Zero);
        });
    }

    [Test]
    public async Task CriticalFailureBreaksTheToolkit()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var door = Door();
            var mob = Human(dexterity: -Strong);
            var toolkit = SEntMan.GetComponent<CFThievesToolkitComponent>(Give(mob, "CFThievesToolkit"));

            // A natural 20 lifts the roll to a plain failure, so allow a few tries.
            for (var i = 0; i < Tries && !toolkit.Broken; i++)
            {
                Rest(mob);
                Assert.That(Locks.TryPick(door, mob), Is.False);
            }

            Assert.That(toolkit.Broken);
            Assert.That(door.Comp.Tampered);

            Rest(mob);
            Assert.That(Locks.TryPick(door, mob), Is.False);
            Assert.That(door.Comp.Successes, Is.Zero);
        });
    }

    [Test]
    public async Task ForceOpenUnlocksAndABrokenLockTakesNoKey()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var door = Door();
            var mob = Human(strength: Strong);
            var key = SEntMan.GetComponent<CFKeyComponent>(SEntMan.SpawnEntity("CFKeyIron", _map.Local(1, 0)));

            Assert.That(Locks.TryForceOpen(door, mob));
            Assert.That(StockLocks.IsLocked(door.Owner), Is.False);

            door.Comp.Broken = true;
            Assert.That(Locks.TryUseKey(door, mob, key), Is.False);
            Assert.That(StockLocks.IsLocked(door.Owner), Is.False);
        });
    }

    [Test]
    public async Task ForceOpenCriticalFailureJamsForThatCreature()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var door = Door();
            var mob = Human(strength: -Strong);

            // A natural 20 lifts the roll to a plain failure, so allow a few tries.
            for (var i = 0; i < Tries && !door.Comp.JammedBy.Contains(mob); i++)
            {
                Rest(mob);
                Assert.That(Locks.TryForceOpen(door, mob), Is.False);
            }

            Assert.That(door.Comp.JammedBy, Does.Contain(mob));
            Assert.That(StockLocks.IsLocked(door.Owner));
        });
    }
}
