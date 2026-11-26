using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Sleep;
using Content.Shared.CCVar;
using Content.Shared.CF;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.GOAP;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
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
    thinkingTimer: 0
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
    armorClass: 1
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

    private bool Awake => SEntMan.System<CFGOAPDriverSystem>().IsAwake(_boss);

    /// <summary>
    /// The boss at 0 and a hero with the test's player in it at <paramref name="heroX"/>. The wake range is 10 feet,
    /// 2 tiles, so the arena's small clear strip can hold both sides of it.
    /// </summary>
    private async Task Setup(int heroX, float sleepSeconds, bool npcs = false)
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(CFCVars.GoapSleep, true);

            // With NPCs off nothing thinks, so nobody moves and only the sleep system touches whether it is awake.
            // With them on, the brain really runs, so the test can see whether it picks up again after waking.
            Server.CfgMan.SetCVar(CCVars.NPCEnabled, npcs);
            _boss = SEntMan.SpawnEntity("CFGOAPSleepTestBoss", _map.Local(0, 0));
            var goap = SEntMan.GetComponent<CFGOAPComponent>(_boss);
            goap.WakeFeet = 10f;
            goap.SleepDelay = TimeSpan.FromSeconds(sleepSeconds);
            if (npcs)
            {
                // Only walking and swinging, so a Demoralize from across the room does not stand in for a fight.
                goap.Actions.Remove("Demoralize");
                goap.Actions.Remove("Feint");
                var sword = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
                Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(_boss, sword, checkActionBlocker: false));
            }

            _hero = SEntMan.SpawnEntity("CFGOAPSleepTestHero", _map.Local(heroX, 0));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, _hero);

            // With the brain running, a hero it could see would keep it awake. It starts as a friend and turns
            // hostile once the boss sleeps (see TurnHostile), which stands in for a player out of sight.
            if (npcs)
                SetFaction("Monster");
        });
    }

    private void SetFaction(string faction)
    {
        var comp = SEntMan.GetComponent<CFFactionComponent>(_hero);
        comp.Faction = faction;
        SEntMan.Dirty(_hero, comp);
    }

    private async Task TurnHostile()
    {
        await Server.WaitPost(() => SetFaction("Alpha"));
    }

    private sealed record Brain(bool Awake, string Goal, int HeroHp, string Facts);

    private async Task<Brain> ReadBrain()
    {
        return await Read(() =>
        {
            var goap = SEntMan.GetComponent<CFGOAPComponent>(_boss);
            var hero = SEntMan.GetComponent<CFCombatantComponent>(_hero);
            return new Brain(Awake, goap.Goal?.Id ?? "none", hero.CurrentHp, ((CFGOAPFact) goap.Facts).ToString());
        });
    }

    /// <summary>Runs up to <paramref name="seconds"/> in half-second steps, stopping once the boss fights.</summary>
    private async Task<Brain> RunUntilItFights(float seconds)
    {
        Brain brain = default!;
        for (var i = 0; i < seconds * 2; i++)
        {
            await Pair.RunTicksSync(15);
            brain = await ReadBrain();
            TestContext.Out.WriteLine($"{i * 0.5f}s: {brain}");
            if (brain.Goal == "KillTarget" && brain.HeroHp < 1000)
                return brain;
        }

        return brain;
    }

    private async Task MoveHero(int x)
    {
        await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>().SetCoordinates(_hero, _map.Local(x, 0)));
    }

    public override async Task DoTeardown()
    {
        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(CFCVars.GoapSleep, false);
            Server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await base.DoTeardown();
    }

    /// <summary>With the brain really running: it sleeps, a player walks up, and it wakes and attacks them.</summary>
    [Test]
    public async Task ItFightsAgainAfterAPlayerWakesIt()
    {
        await Setup(6, sleepSeconds: 1, npcs: true);
        await Pair.RunTicksSync(90);
        var asleep = await ReadBrain();
        Assert.That(asleep.Awake, Is.False, $"It should have slept: {asleep}");
        Assert.That(asleep.Goal, Is.EqualTo("none"), "Sleeping drops the goal.");

        await TurnHostile();
        await MoveHero(1);
        var brain = await RunUntilItFights(10);
        Assert.That(brain.Awake, $"A player stepped in: {brain}");
        Assert.That(brain.Goal, Is.EqualTo("KillTarget"), $"It should fight the player beside it: {brain}");
        Assert.That(brain.HeroHp, Is.LessThan(1000), $"It should have hit the player: {brain}");
    }

    /// <summary>With the brain really running: a hit from out of range wakes it and it comes looking.</summary>
    [Test]
    public async Task ItFightsAgainAfterAHitWakesIt()
    {
        await Setup(6, sleepSeconds: 1, npcs: true);
        await Pair.RunTicksSync(90);
        var asleep = await ReadBrain();
        Assert.That(asleep.Awake, Is.False, $"It should have slept: {asleep}");

        await TurnHostile();
        await Server.WaitPost(() =>
        {
            var hurt = new CFDamagedEvent(_hero, 3);
            SEntMan.EventBus.RaiseLocalEvent(_boss, ref hurt);
        });
        var brain = await RunUntilItFights(15);
        Assert.That(brain.Awake, $"Being hurt wakes it: {brain}");
        Assert.That(brain.Goal, Is.EqualTo("KillTarget"), $"It should go for whoever hurt it: {brain}");
        Assert.That(brain.HeroHp, Is.LessThan(1000), $"It should have reached and hit the player: {brain}");
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
