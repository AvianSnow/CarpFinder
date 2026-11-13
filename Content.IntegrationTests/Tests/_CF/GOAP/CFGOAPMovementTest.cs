using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.IntegrationTests.Tests.CF.GOAP.DecisionLog;
using Content.Server.CF.Companions;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Sensors;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.GOAP;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.CF.GOAP;

/// <summary>
/// How GOAP creatures move: idle walks with rests between them, closing on a target without running past it, swinging
/// on the way in, and companions keeping behind their master and obeying pointing.
/// </summary>
[TestFixture]
[TestOf(typeof(CFGOAPSystem))]
public sealed class CFGOAPMovementTest : CFGOAPLoggedTest
{
    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: [ MobHuman, CFGOAPBrain ]
  id: CFMovementTestBoss
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
  id: CFMovementTestHero
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

    private SharedTransformSystem Xform => SEntMan.System<SharedTransformSystem>();

    private IConfigurationManager Config => Server.ResolveDependency<IConfigurationManager>();

    private IGameTiming Timing => Server.ResolveDependency<IGameTiming>();

    private Vector2 Position(EntityUid uid) => Xform.GetMapCoordinates(uid).Position;

    private float Speed(EntityUid uid) => SEntMan.GetComponent<PhysicsComponent>(uid).LinearVelocity.Length();

    private async Task Load()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitPost(() => Config.SetCVar(CCVars.NPCEnabled, true));
    }

    private async Task NpcsOff()
    {
        await Server.WaitPost(() => Config.SetCVar(CCVars.NPCEnabled, false));
    }

    private Entity<CFGOAPComponent> Boss(int x, bool armed = true)
    {
        var boss = SEntMan.SpawnEntity("CFMovementTestBoss", _map.Local(x, 0));
        var goap = SEntMan.GetComponent<CFGOAPComponent>(boss);

        // Only walking and swinging, so a Demoralize from across the room does not stand in for closing in.
        goap.Actions.Remove("Demoralize");
        goap.Actions.Remove("Feint");
        if (armed)
        {
            var sword = SEntMan.SpawnEntity("CFLongsword", _map.Local(x, 0));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(boss, sword, checkActionBlocker: false));
        }

        Wake(boss);
        return (boss, goap);
    }

    private void Wake(EntityUid uid)
    {
        SEntMan.System<SharedPhysicsSystem>().WakeBody(uid);
        SEntMan.System<NPCSystem>().WakeNPC(uid);
    }

    /// <summary>Runs the game a sixth of a second at a time, calling <paramref name="sample"/> after each.</summary>
    private async Task Sample(float seconds, Action sample)
    {
        for (var i = 0; i < seconds * 6; i++)
        {
            await Pair.RunTicksSync(Timing.TickRate / 6);
            await Server.WaitPost(sample);
        }
    }

    [Test]
    public async Task IdleItWalksSomewhereThenRests()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        await Server.WaitPost(() =>
        {
            boss = Boss(3, armed: false);
            boss.Comp.HomeRange = 3f;
        });

        var moving = 0;
        var resting = 0;
        var furthest = 0f;
        var restAfterWalk = false;
        Vector2 home = default;
        await Server.WaitPost(() => home = Position(boss));
        await Sample(14f, () =>
        {
            var speed = Speed(boss);
            if (speed > 1f)
                moving++;
            else if (speed < 0.05f)
            {
                resting++;
                restAfterWalk |= moving > 0;
            }

            furthest = MathF.Max(furthest, Vector2.Distance(Position(boss), home));
        });

        await NpcsOff();
        Assert.That(moving, Is.GreaterThan(0), "It goes for a walk.");
        Assert.That(restAfterWalk, "It stops and stands once it gets there.");
        Assert.That(resting, Is.GreaterThan(6), "And stands for a while, not a tick.");
        Assert.That(furthest, Is.LessThan(3.5f), "It keeps to its home range.");
    }

    [Test]
    public async Task ItStopsBesideAStandingTargetWithoutRunningPast()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("CFMovementTestHero", _map.Local(5, 0));
            boss = Boss(0);
        });

        var closest = float.MaxValue;
        var furthestAlong = float.MinValue;
        var heroX = 0f;
        await Server.WaitPost(() => heroX = Position(hero).X);
        await Sample(4f, () =>
        {
            closest = MathF.Min(closest, Vector2.Distance(Position(boss), Position(hero)));
            furthestAlong = MathF.Max(furthestAlong, Position(boss).X);
        });

        var distance = 0f;
        var speed = 0f;
        await Server.WaitPost(() =>
        {
            distance = Vector2.Distance(Position(boss), Position(hero));
            speed = Speed(boss);
        });

        await NpcsOff();
        Assert.That(furthestAlong, Is.LessThan(heroX), "It never runs past them.");
        Assert.That(closest, Is.GreaterThan(0.6f), "Nor into their body.");
        Assert.That(distance, Is.LessThan(1.25f), "It ends within reach.");
        Assert.That(speed, Is.LessThan(0.5f), "And stands there to fight.");
    }

    [Test]
    public async Task ItSwingsOnTheWayIn()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        var readyWhileClosing = false;
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity("CFMovementTestHero", _map.Local(5, 0));
            boss = Boss(0);
        });

        await Sample(1.5f, () =>
        {
            if (boss.Comp.StepStarted
                && boss.Comp.Step < boss.Comp.Plan.Count
                && boss.Comp.Plan[boss.Comp.Step] == "Close")
                readyWhileClosing |= SEntMan.HasComponent<NPCMeleeCombatComponent>(boss);
        });

        await NpcsOff();
        Assert.That(readyWhileClosing, "Stock melee is ready while it closes, so it swings the moment it is in reach.");
    }

    [Test]
    public async Task ItKeepsUpWithATargetWalkingAway()
    {
        await Load();
        Entity<CFGOAPComponent> boss = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("CFMovementTestHero", _map.Local(1, 0));
            boss = Boss(0);
        });

        await Pair.RunTicksSync(30);

        // The hero walks east at about two tiles a second, slower than a mob runs.
        var walked = 0;
        var furthest = 0f;
        for (var i = 0; i < 60; i++)
        {
            await Server.WaitPost(() =>
            {
                Xform.SetCoordinates(hero, _map.Local(1, 0).Offset(new Vector2(walked * 0.07f, 0f)));
                walked++;
            });
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() => furthest = MathF.Max(furthest, Vector2.Distance(Position(boss), Position(hero))));
        }

        await Pair.RunTicksSync(15);
        var distance = 0f;
        await Server.WaitPost(() => distance = Vector2.Distance(Position(boss), Position(hero)));
        await NpcsOff();

        Assert.That(furthest, Is.LessThan(2.5f), "It walks with them rather than waiting to start a new chase.");
        Assert.That(distance, Is.LessThan(1.25f), "Once they stop it is back in reach.");
    }

    [Test]
    public async Task ACompanionStopsBehindItsMasterAndStaysThere()
    {
        await Load();
        EntityUid hero = default, companion = default;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("CFMovementTestHero", _map.Local(0, 0));
            companion = SEntMan.SpawnEntity("CFSummon", _map.Local(4, 0));
            SEntMan.System<CFCompanionSystem>().Bind(hero, companion);
            Wake(companion);
        });

        await Pair.RunTicksSync(Timing.TickRate * 4);

        Vector2 settledAt = default;
        var distance = 0f;
        var goal = string.Empty;
        await Server.WaitPost(() =>
        {
            settledAt = Position(companion);
            distance = Vector2.Distance(settledAt, Position(hero));
            goal = SEntMan.GetComponent<CFGOAPComponent>(companion).Goal?.Id ?? string.Empty;
        });

        var drift = 0f;
        await Sample(2f, () => drift = MathF.Max(drift, Vector2.Distance(Position(companion), settledAt)));

        // The master moves off. It follows and stops behind them again.
        await Server.WaitPost(() => Xform.SetCoordinates(hero, _map.Local(6, 0)));
        await Pair.RunTicksSync(Timing.TickRate * 4);
        var after = 0f;
        var speed = 0f;
        await Server.WaitPost(() =>
        {
            after = Vector2.Distance(Position(companion), Position(hero));
            speed = Speed(companion);
        });

        await NpcsOff();

        Assert.That(goal, Is.EqualTo("StayWithMaster"));
        Assert.That(distance, Is.InRange(0.9f, 2.3f), "It comes to stand a little behind its master.");
        Assert.That(drift, Is.LessThan(0.2f), "And stays put while they do, without stepping back and forth.");
        Assert.That(after, Is.InRange(0.9f, 2.3f), "When they move off, it catches up.");
        Assert.That(speed, Is.LessThan(0.1f), "And stops behind them again.");
    }

    [Test]
    public async Task ARecalledCompanionLeavesTheFightAlone()
    {
        await Load();
        EntityUid hero = default, companion = default;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("CFMovementTestHero", _map.Local(0, 0));
            companion = SEntMan.SpawnEntity("CFSummon", _map.Local(6, 0));
            var foe = SEntMan.SpawnEntity("CFMovementTestBoss", _map.Local(7, 0));
            var companions = SEntMan.System<CFCompanionSystem>();
            companions.Bind(hero, companion);
            companions.OrderAttack(companion, foe);
            companions.ComeAlong(companion);
            Wake(companion);
        });

        // Short enough that it is still on its way back, where a recall still holds.
        var fought = false;
        await Sample(0.6f, () =>
        {
            var brain = SEntMan.GetComponent<CFGOAPComponent>(companion);
            fought |= brain.Goal?.Id == "KillTarget" || brain.Target != null;
        });

        await NpcsOff();
        Assert.That(fought, Is.False, "Called back, it goes back and ignores the enemy beside it.");
    }

    [Test]
    public async Task AnOrderedCompanionIsNotLeashedBack()
    {
        await Load();
        var leftLair = true;
        var returning = true;
        await Server.WaitPost(() =>
        {
            Config.SetCVar(CCVars.NPCEnabled, false);
            var hero = SEntMan.SpawnEntity("CFMovementTestHero", _map.Local(0, 0));
            var companion = SEntMan.SpawnEntity("CFSummon", _map.Local(3, 0));
            var foe = SEntMan.SpawnEntity("CFMovementTestBoss", _map.Local(5, 0));
            var companions = SEntMan.System<CFCompanionSystem>();
            companions.Bind(hero, companion);
            companions.OrderAttack(companion, foe);

            // Long past its chase time, which would send any other companion back.
            var lair = SEntMan.GetComponent<CFGOAPLairComponent>(companion);
            lair.AwaySince = SGameTiming.CurTime - TimeSpan.FromMinutes(5);

            var brain = new Entity<CFGOAPComponent>(companion, SEntMan.GetComponent<CFGOAPComponent>(companion));
            SEntMan.System<CFGOAPSystem>().Think(brain);
            leftLair = CFGOAPSystem.Knows(brain.Comp, CFGOAPFact.LeftLair);
            returning = lair.Returning;
        });

        Assert.That(leftLair, Is.False, "Pointing is an order, so the leash waits.");
        Assert.That(returning, Is.False);
    }
}
