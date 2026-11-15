using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.Effects;

/// <summary>
/// Areas that fill with something seen (<c>CFAreaFill</c>, <c>CFAreaFillSystem</c>): Fireball's flames, Grease's
/// puddles, and the splash a single hit leaves.
/// </summary>
[TestFixture]
public sealed class CFAreaFillTest : GameTest
{
    private const int AlwaysFailDc = 60;

    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFFillTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 1000
    currentHp: 1000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFFillTestHero
  id: CFFillTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private EntityUid Spell(string prototype)
    {
        var action = SEntMan.SpawnEntity(prototype, MapCoordinates.Nullspace);
        var check = SEntMan.GetComponent<CFCheckComponent>(action);
        check.DcSource = CFDcSource.Fixed;
        check.FixedDc = AlwaysFailDc;
        return action;
    }

    private MapCoordinates Aim(int x, int y)
    {
        return SEntMan.System<SharedTransformSystem>().ToMapCoordinates(_map.Local(x, y));
    }

    private int Count(string prototype)
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == prototype)
                count++;
        }

        return count;
    }

    /// <summary>Puddles holding this reagent. Other tests' puddles dry up while this one runs, so they are not counted.</summary>
    private int Puddles(string reagent = "CFGrease")
    {
        var count = 0;
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();
        var query = SEntMan.AllEntityQueryEnumerator<PuddleComponent>();
        while (query.MoveNext(out var uid, out var puddle))
        {
            if (solutions.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution, out var solution)
                && solution.GetTotalPrototypeQuantity(reagent) > 0)
            {
                count++;
            }
        }

        return count;
    }

    private int Hurt(EntityUid uid)
    {
        return 1000 - SEntMan.GetComponent<CFCombatantComponent>(uid).CurrentHp;
    }

    /// <summary>Fireball hurts at once, and its flames roll out from the middle and then burn out.</summary>
    [Test]
    public async Task FireballLeavesFlamesThatBurnOut()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid foe = default;
        var before = 0;
        var atOnce = 0;
        var hurtAtOnce = 0;
        await Server.WaitPost(() =>
        {
            before = Count("CFFillFlame");
            var caster = SEntMan.SpawnAttachedTo("CFFillTestHero", _map.Local(0, 0));
            foe = SEntMan.SpawnAttachedTo("CFFillTestFoe", _map.Local(3, 0));
            SEntMan.System<CFEffectSystem>().ResolveArea(Spell("CFFireball"), caster, Aim(3, 0));
            atOnce = Count("CFFillFlame") - before;
            hurtAtOnce = Hurt(foe);
        });

        await RunSeconds(0.6f);
        var spread = 0;
        await Server.WaitPost(() => spread = Count("CFFillFlame") - before);

        await RunSeconds(3.5f);
        var left = -1;
        await Server.WaitPost(() => left = Count("CFFillFlame") - before);

        Assert.Multiple(() =>
        {
            Assert.That(hurtAtOnce, Is.GreaterThan(0), "The save is rolled as it is cast.");
            Assert.That(atOnce, Is.GreaterThan(0), "The middle is alight at once.");
            Assert.That(spread, Is.GreaterThan(atOnce), "The flames roll outwards.");
            Assert.That(left, Is.Zero, "They burn out.");
        });
    }

    /// <summary>
    /// Grease: puddles for a minute. Whoever stands there falls as it is cast. After that, running on it means another
    /// save and walking does not, and the puddles go when the spell ends.
    /// </summary>
    [Test]
    public async Task GreaseTripsRunnersUntilItEnds()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var conditions = SEntMan.System<CFConditionSystem>();
        var physics = SEntMan.System<SharedPhysicsSystem>();
        EntityUid first = default;
        EntityUid walker = default;
        EntityUid runner = default;
        var before = 0;
        var firstProne = false;
        await Server.WaitPost(() =>
        {
            before = Puddles();
            var caster = SEntMan.SpawnAttachedTo("CFFillTestHero", _map.Local(0, 0));
            first = SEntMan.SpawnAttachedTo("CFFillTestFoe", _map.Local(3, 0));
            walker = SEntMan.SpawnAttachedTo("CFFillTestFoe", _map.Local(0, 1));
            runner = SEntMan.SpawnAttachedTo("CFFillTestFoe", _map.Local(1, 1));
            SEntMan.System<CFEffectSystem>().ResolveArea(Spell("CFGrease"), caster, Aim(3, 0));
            firstProne = conditions.IsProne(first);
        });

        await RunSeconds(0.5f);
        var puddles = 0;
        await Server.WaitPost(() =>
        {
            puddles = Puddles() - before;
            var transform = SEntMan.System<SharedTransformSystem>();
            transform.SetCoordinates(walker, _map.Local(3, 1));
            transform.SetCoordinates(runner, _map.Local(4, 0));
        });

        // Keep them moving across a few watches: one slowly, one at a run.
        for (var i = 0; i < 8; i++)
        {
            await Server.WaitPost(() =>
            {
                physics.SetLinearVelocity(walker, new Vector2(0f, 0.5f));
                physics.SetLinearVelocity(runner, new Vector2(0f, 4.5f));
                SEntMan.System<SharedTransformSystem>().SetCoordinates(runner, _map.Local(4, 0));
            });
            await Pair.RunTicksSync(3);
        }

        var walkerProne = true;
        var runnerProne = false;
        await Server.WaitPost(() =>
        {
            walkerProne = conditions.IsProne(walker);
            runnerProne = conditions.IsProne(runner);
        });

        await RunSeconds(61f);
        var left = -1;
        await Server.WaitPost(() => left = Puddles() - before);

        Assert.Multiple(() =>
        {
            Assert.That(firstProne, "Standing in it when it is cast: a failed save is prone.");
            Assert.That(puddles, Is.EqualTo(4), "A 10-foot square of puddles.");
            Assert.That(walkerProne, Is.False, "Walking on it needs no save.");
            Assert.That(runnerProne, "Running on it does.");
            Assert.That(left, Is.Zero, "The grease goes when the spell ends.");
        });
    }

    /// <summary>A splash is only seen: one puddle where it lands.</summary>
    [Test]
    public async Task ASplashLeavesAPuddle()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        var made = 0;
        await Server.WaitPost(() =>
        {
            var before = Puddles("CFAcidSplash");
            var caster = SEntMan.SpawnAttachedTo("CFFillTestHero", _map.Local(0, 0));
            var fill = new CFFill { Reagent = "CFAcidSplash", UnitsPerTile = 15f, SpreadSeconds = 0f };
            SEntMan.System<CFAreaFillSystem>().Splash(caster, caster, Aim(2, 0), CFAreaShape.Square, 5, fill);
            made = Puddles("CFAcidSplash") - before;
        });

        Assert.That(made, Is.EqualTo(1));
    }
}
