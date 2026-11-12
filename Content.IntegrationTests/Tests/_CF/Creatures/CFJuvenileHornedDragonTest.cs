using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Sensors;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CCVar;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Robust.Shared.Configuration;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Graphics.RSI;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>
/// The Juvenile Horned Dragon: its immunities, its sprite made for CarpFinder, Poison Breath, Draconic Momentum, and how
/// it keeps its lair.
/// </summary>
[TestFixture]
public sealed class CFJuvenileHornedDragonTest : GameTest
{
    private const string Dragon = "CFMobJuvenileHornedDragon";

    private static readonly Box2i Footprint = new(-1, -1, 2, 2);
    private static readonly Box2i LairFootprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    // Reflex -10, so even a natural 20 only gets a success against DC 21: the breath always deals some damage.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFDragonTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 1
    maxHp: 1000
    currentHp: 1000
    printedReflex: -10
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

# Always a critical hit against AC 1, except on a natural 1.
- type: entity
  parent: CFMobJuvenileHornedDragon
  id: CFDragonTestCritter
  components:
  - type: CFCombatant
    level: 4
    maxHp: 60
    currentHp: 60
    diesAtZeroHp: true
    printedAttack: 40
";

    private CFDevMap _map = default!;

    private Entity<CFGOAPComponent> Brain(EntityUid uid)
    {
        return (uid, SEntMan.GetComponent<CFGOAPComponent>(uid));
    }

    private List<string> Plan(Entity<CFGOAPComponent> mob)
    {
        SEntMan.System<CFGOAPSystem>().Think(mob);
        return mob.Comp.Plan.Select(step => step.Id).ToList();
    }

    private Entity<ActionComponent> Breath(EntityUid dragon)
    {
        Assert.That(SEntMan.System<CFBreathWeaponSystem>().TryGetBreath(dragon, out var breath), "It has a breath weapon.");
        return breath!.Value;
    }

    /// <summary>A dragon that only does what the test tells it: left to think, it walks up and bites the hero.</summary>
    private EntityUid SpawnStillDragon(int x, int y)
    {
        var dragon = SEntMan.SpawnAttachedTo(Dragon, _map.Local(x, y));
        SEntMan.RemoveComponent<HTNComponent>(dragon);
        SEntMan.RemoveComponent<CFGOAPComponent>(dragon);
        return dragon;
    }

    private void SpendBreath(EntityUid dragon)
    {
        SEntMan.System<SharedActionsSystem>().SetCooldown(Breath(dragon).Owner, TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task PoisonAndSleepDoNotTouchIt()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var hp = 0;
        var maxHp = -1;
        var asleep = true;
        await Server.WaitPost(() =>
        {
            var dragon = SEntMan.SpawnEntity(Dragon, map.Local(0, 0));
            var combat = SEntMan.GetComponent<CFCombatantComponent>(dragon);
            var conditions = SEntMan.System<CFConditionSystem>();

            SEntMan.System<CFEffectSystem>().DealDamage(dragon, dragon, 30, "Poison", false);
            conditions.ApplyCondition(dragon, CFConditionKind.Asleep, 0, 6f, dragon, 0);

            hp = combat.CurrentHp;
            maxHp = combat.MaxHp;
            asleep = conditions.IsProne(dragon);
        });

        Assert.Multiple(() =>
        {
            Assert.That(hp, Is.EqualTo(maxHp), "Poison damage does nothing.");
            Assert.That(asleep, Is.False, "It cannot be put to sleep.");
        });
    }

    /// <summary>The sheet's rows are south, north, east, west, as the RSI wants them, and each animates.</summary>
    [Test]
    public async Task ItsSpriteLoads()
    {
        var hasAlive = false;
        var hasDead = false;
        var directions = RsiDirectionType.Dir1;
        var frames = 0;
        await Client.WaitPost(() =>
        {
            var cache = Client.ResolveDependency<IResourceCache>();
            if (!cache.TryGetResource<RSIResource>(new ResPath("/Textures/_CF/Mobs/Monsters/Dragon/horned_dragon.rsi"), out var rsi))
                return;

            hasAlive = rsi.RSI.TryGetState("alive", out var alive);
            hasDead = rsi.RSI.TryGetState("dead", out _);
            if (alive == null)
                return;

            directions = alive.RsiDirections;
            frames = alive.DelayCount;
        });

        Assert.Multiple(() =>
        {
            Assert.That(hasAlive);
            Assert.That(hasDead);
            Assert.That(directions, Is.EqualTo(RsiDirectionType.Dir4));
            Assert.That(frames, Is.EqualTo(4));
        });
    }

    /// <summary>This dragon's cloud, while it is in the air. Other tests on the same server may have their own.</summary>
    private CFBreathCloudComponent CloudOf(EntityUid dragon)
    {
        var query = SEntMan.AllEntityQueryEnumerator<CFBreathCloudComponent>();
        while (query.MoveNext(out _, out var cloud))
        {
            if (cloud.User == dragon)
                return cloud;
        }

        return null;
    }

    /// <summary>How many tiles of this dragon's cloud are drawn.</summary>
    private int CloudTiles(EntityUid dragon)
    {
        if (CloudOf(dragon) is not { } cloud)
            return 0;

        return cloud.Clouds.Count(tile => tile is { } uid && SEntMan.EntityExists(uid));
    }

    private int Hurt(EntityUid hero)
    {
        return 1000 - SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
    }

    /// <summary>
    /// Poison Breath billows out as a cloud. Nobody rolls the moment it breathes. The hero rolls once the gas on their
    /// tile has thickened, and only once however long they stand in it. Then the cloud thins out and is gone.
    /// </summary>
    [Test]
    public async Task PoisonBreathBillowsOutAndEachCreatureRollsOnce()
    {
        _map = await CFDevMap.Load(Pair, LairFootprint, avoidStructures: true);
        EntityUid dragon = default;
        EntityUid hero = default;
        var hurtAtOnce = -1;
        var spent = 0;
        var first = false;
        var second = true;
        var recharge = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            dragon = SpawnStillDragon(0, 0);
            hero = SEntMan.SpawnAttachedTo("CFDragonTestHero", _map.Local(3, 0));
            var breathing = SEntMan.System<CFBreathWeaponSystem>();
            var pool = SEntMan.EnsureComponent<CFActionPoolComponent>(dragon);
            var before = SEntMan.System<CFActionPoolSystem>().ReadyCount(dragon, pool);
            var breath = Breath(dragon);
            var aim = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);

            first = breathing.TryBreathe(dragon, breath, aim);
            second = breathing.TryBreathe(dragon, breath, aim);

            hurtAtOnce = Hurt(hero);
            spent = before - SEntMan.System<CFActionPoolSystem>().ReadyCount(dragon, pool);
            if (breath.Comp.Cooldown is { } cooldown)
                recharge = cooldown.End - cooldown.Start;
        });

        await RunSeconds(0.2f);
        var cloudsEarly = 0;
        var hurtEarly = -1;
        await Server.WaitPost(() =>
        {
            cloudsEarly = CloudTiles(dragon);
            hurtEarly = Hurt(hero);
        });

        await RunSeconds(2.3f);
        var cloudsFilled = 0;
        var drawn = new List<EntityUid>();
        var hurt = 0;
        await Server.WaitPost(() =>
        {
            cloudsFilled = CloudTiles(dragon);
            drawn.AddRange(CloudOf(dragon)?.Clouds.Where(tile => tile != null).Select(tile => tile.Value) ?? Enumerable.Empty<EntityUid>());
            hurt = Hurt(hero);
        });

        await RunSeconds(2f);
        var hurtLater = 0;
        await Server.WaitPost(() => hurtLater = Hurt(hero));

        await RunSeconds(12f);
        var cloudsLeft = -1;
        var breathsLeft = -1;
        await Server.WaitPost(() =>
        {
            cloudsLeft = drawn.Count(SEntMan.EntityExists);
            breathsLeft = CloudOf(dragon) == null ? 0 : 1;
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, "It breathes.");
            Assert.That(spent, Is.EqualTo(2), "Breathing is 2 actions.");
            Assert.That(recharge.TotalSeconds, Is.InRange(6, 24), "1d4 rounds.");
            Assert.That(second, Is.False, "It cannot breathe again until it recharges.");
            Assert.That(hurtAtOnce, Is.Zero, "Nobody rolls the moment it breathes.");
            Assert.That(hurtEarly, Is.Zero, "Nor before the gas on their tile has thickened.");
            Assert.That(cloudsEarly, Is.InRange(1, cloudsFilled - 1), "The cloud spreads out tile by tile.");
            Assert.That(cloudsFilled, Is.GreaterThanOrEqualTo(15), "It fills most of a 30-foot cone.");
            Assert.That(hurt, Is.InRange(2, 60), "5d6 poison, halved at best.");
            Assert.That(hurtLater, Is.EqualTo(hurt), "Standing in it longer is not a second roll.");
            Assert.That(cloudsLeft, Is.Zero, "The cloud thins out and is gone.");
            Assert.That(breathsLeft, Is.Zero);
        });
    }

    /// <summary>Someone who walks into the cloud while it lingers rolls too.</summary>
    [Test]
    public async Task WalkingIntoTheLingeringCloudRolls()
    {
        _map = await CFDevMap.Load(Pair, LairFootprint, avoidStructures: true);
        EntityUid hero = default;
        var breathed = false;
        await Server.WaitPost(() =>
        {
            var dragon = SpawnStillDragon(0, 0);

            // Behind the dragon, out of the cone.
            hero = SEntMan.SpawnAttachedTo("CFDragonTestHero", _map.Local(-1, 0));
            var aim = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(_map.Local(3, 0));
            breathed = SEntMan.System<CFBreathWeaponSystem>().TryBreathe(dragon, Breath(dragon), aim);
        });

        await RunSeconds(2.5f);
        var hurtOutside = -1;
        await Server.WaitPost(() =>
        {
            hurtOutside = Hurt(hero);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, _map.Local(3, 0));
        });

        await RunSeconds(0.5f);
        var hurtInside = 0;
        await Server.WaitPost(() => hurtInside = Hurt(hero));

        Assert.Multiple(() =>
        {
            Assert.That(breathed);
            Assert.That(hurtOutside, Is.Zero, "Out of the cone, the cloud never reached them.");
            Assert.That(hurtInside, Is.InRange(2, 60), "Walking into it, they rolled.");
        });
    }

    [Test]
    public async Task ACriticalHitRechargesTheBreath()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var charged = false;
        await Server.WaitPost(() =>
        {
            var dragon = SEntMan.SpawnEntity("CFDragonTestCritter", _map.Local(0, 0));
            var hero = SEntMan.SpawnEntity("CFDragonTestHero", _map.Local(1, 0));
            SpendBreath(dragon);
            var strikes = SEntMan.System<CFStrikeSystem>();
            var actions = SEntMan.System<SharedActionsSystem>();
            for (var i = 0; i < 5 && actions.IsCooldownActive(Breath(dragon).Comp); i++)
            {
                strikes.PerformStrike(dragon, hero, strikes.UnarmedStats(dragon), dragon, melee: true, countMap: false);
            }

            charged = !actions.IsCooldownActive(Breath(dragon).Comp);
        });

        Assert.That(charged, "Draconic Momentum recharged it.");
    }

    [Test]
    public async Task ItBreathesWhenItsBreathIsCharged()
    {
        _map = await CFDevMap.Load(Pair, LairFootprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var mob = Brain(SEntMan.SpawnEntity(Dragon, _map.Local(0, 0)));
            SEntMan.SpawnEntity("CFDragonTestHero", _map.Local(5, 0));
            SEntMan.System<CFGOAPSystem>().Start(mob);

            Assert.That(Plan(mob), Is.EqualTo(new[] { "Breathe" }));
        });
    }

    /// <summary>While the breath recharges it waits for what is far away, and goes for what comes within 3 tiles.</summary>
    [TestCase(5, new[] { "Await" })]
    [TestCase(2, new[] { "Close", "Strike" })]
    public async Task WhileItRechargesItOnlyGoesForWhatComesClose(int tiles, string[] plan)
    {
        _map = await CFDevMap.Load(Pair, LairFootprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var mob = Brain(SEntMan.SpawnEntity(Dragon, _map.Local(0, 0)));
            SEntMan.SpawnEntity("CFDragonTestHero", _map.Local(tiles, 0));
            SpendBreath(mob);
            SEntMan.System<CFGOAPSystem>().Start(mob);

            Assert.That(Plan(mob), Is.EqualTo(plan));
        });
    }

    /// <summary>Away from its lair too long, it goes back, unless an enemy is in reach.</summary>
    [TestCase(4, true)]
    [TestCase(1, false)]
    public async Task ItGoesBackToItsLair(int heroTiles, bool goesHome)
    {
        _map = await CFDevMap.Load(Pair, LairFootprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity(Dragon, _map.Local(0, 0));
            var mob = Brain(uid);
            SEntMan.System<CFGOAPSystem>().Start(mob);
            SEntMan.System<CFGOAPSystem>().Think(mob);

            // Home is where it first thought. Now it has wandered off, and been away longer than it likes.
            SEntMan.System<SharedTransformSystem>().SetCoordinates(uid, _map.Local(5, 0));
            SEntMan.GetComponent<CFGOAPLairComponent>(uid).ChaseTime = TimeSpan.Zero;
            SEntMan.SpawnEntity("CFDragonTestHero", _map.Local(5 - heroTiles, 0));
            SpendBreath(uid);

            var plan = Plan(mob);
            if (goesHome)
            {
                Assert.That(mob.Comp.Goal?.Id, Is.EqualTo("ReturnToLair"));
                Assert.That(plan, Is.EqualTo(new[] { "GoHome" }));
            }
            else
            {
                Assert.That(mob.Comp.Goal?.Id, Is.EqualTo("KillTarget"), "It fights what is in reach where it stands.");
            }
        });
    }

    [Test]
    public async Task ItBreathesOnAnIntruderAndKeepsToItsLair()
    {
        _map = await CFDevMap.Load(Pair, LairFootprint, avoidStructures: true);
        EntityUid dragon = default;
        EntityUid hero = default;
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() =>
        {
            dragon = SEntMan.SpawnEntity(Dragon, _map.Local(0, 0));
            hero = SEntMan.SpawnEntity("CFDragonTestHero", _map.Local(5, 0));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(dragon);
            SEntMan.System<SharedPhysicsSystem>().WakeBody(hero);
            SEntMan.System<NPCSystem>().WakeNPC(dragon);
            config.SetCVar(CCVars.NPCEnabled, true);
        });

        await Pair.RunTicksSync(300);

        var hp = 0;
        var moved = 0f;
        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, false);
            hp = SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
            var transform = SEntMan.System<SharedTransformSystem>();
            moved = (transform.GetWorldPosition(dragon) - transform.ToMapCoordinates(_map.Local(0, 0)).Position).Length();
        });

        Assert.Multiple(() =>
        {
            Assert.That(hp, Is.LessThan(1000), "It breathed on them.");
            Assert.That(moved, Is.LessThan(1.5f), "It stayed in its lair: they were too far to go after.");
        });
    }
}
