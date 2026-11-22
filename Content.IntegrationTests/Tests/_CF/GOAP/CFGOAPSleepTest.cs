using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Sleep;
using Content.Shared.CCVar;
using Content.Shared.CF;
using Content.Shared.CF.Combat;
using Content.Shared.CF.GOAP;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>GOAP brains sleep with no player near, and wake when one comes.</summary>
[TestFixture]
[TestOf(typeof(CFGOAPSleepSystem))]
public sealed class CFGOAPSleepTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: [ MobHuman, CFGOAPBrain ]
  id: CFGOAPSleepTestBoss
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Monster

- type: entity
  parent: MobHuman
  id: CFGOAPSleepTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 40
    maxHp: 1000
    currentHp: 1000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;
    private EntityUid _boss;
    private EntityUid _hero;

    private bool Awake => SEntMan.HasComponent<ActiveNPCComponent>(_boss);

    /// <summary>
    /// The boss at 0 and a hero with the test's player in it at <paramref name="heroX"/>. The wake range is 10 feet,
    /// 2 tiles, so the arena's small clear strip can hold both sides of it.
    /// </summary>
    private async Task Setup(int heroX, float sleepSeconds)
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(CFCVars.GoapSleep, true);

            // Nothing thinks, so nobody moves and only the sleep system touches whether it is awake.
            Server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            _boss = SEntMan.SpawnEntity("CFGOAPSleepTestBoss", _map.Local(0, 0));
            var goap = SEntMan.GetComponent<CFGOAPComponent>(_boss);
            goap.WakeFeet = 10f;
            goap.SleepDelay = TimeSpan.FromSeconds(sleepSeconds);

            _hero = SEntMan.SpawnEntity("CFGOAPSleepTestHero", _map.Local(heroX, 0));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, _hero);
        });
    }

    private async Task MoveHero(int x)
    {
        await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>().SetCoordinates(_hero, _map.Local(x, 0)));
    }

    public override async Task DoTeardown()
    {
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CFCVars.GoapSleep, false));
        await base.DoTeardown();
    }

    [Test]
    public async Task ItSleepsAwayFromPlayersAndWakesWhenOneComes()
    {
        await Setup(6, sleepSeconds: 2);

        await Pair.RunTicksSync(30);
        Assert.That(await Read(() => Awake), "It waits out the delay before sleeping.");

        await Pair.RunTicksSync(60);
        Assert.That(await Read(() => Awake), Is.False, "No player within 10 feet for 2 seconds.");

        await MoveHero(1);
        await Pair.RunTicksSync(20);
        Assert.That(await Read(() => Awake), "A player stepped in.");

        // Out again: a player going in and out does not flip it.
        await MoveHero(6);
        await Pair.RunTicksSync(30);
        Assert.That(await Read(() => Awake), "One second out is inside the delay.");
        await MoveHero(1);
        await Pair.RunTicksSync(15);
        await MoveHero(6);
        await Pair.RunTicksSync(45);
        Assert.That(await Read(() => Awake), "Stepping back in restarted the delay.");

        await Pair.RunTicksSync(45);
        Assert.That(await Read(() => Awake), Is.False);
    }

    [Test]
    public async Task ItStaysAwakeWhileItCanSeeAnEnemy()
    {
        await Setup(6, sleepSeconds: 1);
        await Server.WaitPost(() => SEntMan.GetComponent<CFGOAPComponent>(_boss).Facts = (ulong) CFGOAPFact.TargetVisible);

        await Pair.RunTicksSync(90);
        Assert.That(await Read(() => Awake), "Freezing mid-fight would look wrong.");

        await Server.WaitPost(() => SEntMan.GetComponent<CFGOAPComponent>(_boss).Facts = 0);
        await Pair.RunTicksSync(30);
        Assert.That(await Read(() => Awake), Is.False);
    }

    [Test]
    public async Task BeingHurtFromOutOfRangeWakesIt()
    {
        await Setup(6, sleepSeconds: 1);
        await Pair.RunTicksSync(75);
        Assert.That(await Read(() => Awake), Is.False);

        await Server.WaitPost(() =>
        {
            var hurt = new CFDamagedEvent(_hero, 3);
            SEntMan.EventBus.RaiseLocalEvent(_boss, ref hurt);
        });
        Assert.That(await Read(() => Awake));
        Assert.That(await Read(() => SEntMan.GetComponent<CFGOAPComponent>(_boss).Lkp), Is.Not.Null, "It goes to look.");
    }

    [Test]
    public async Task APlayerDoesNotWakeTheDead()
    {
        await Setup(1, sleepSeconds: 1);
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(_boss, MobState.Dead));

        await Pair.RunTicksSync(30);
        Assert.That(await Read(() => Awake), Is.False);
    }

    private async Task<T> Read<T>(Func<T> read)
    {
        var value = default(T)!;
        await Server.WaitPost(() => value = read());
        return value;
    }
}
