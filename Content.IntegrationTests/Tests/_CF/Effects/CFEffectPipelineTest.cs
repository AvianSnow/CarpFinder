using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Effects;

/// <summary>
/// One spell per delivery through the Phase 1 effect pipeline (<see cref="CFEffectSystem"/>).
/// Runs on the dev map (<c>Resources/Prototypes/Maps/debug.yml</c>). Checks are pinned with a fixed DC
/// so the d20 cannot change which targets are affected.
/// </summary>
[TestFixture]
[TestOf(typeof(CFEffectSystem))]
[TestOf(typeof(CFSpellSystem))]
public sealed class CFEffectPipelineTest : GameTest
{
    private const int StartHp = 200;

    /// <summary>A DC no untrained creature can reach: every save fails (a natural 20 only lifts it to failure).</summary>
    private const int AlwaysFailDc = 60;

    /// <summary>A DC every save beats by 10: critical success (a natural 1 only drops it to success).</summary>
    private const int AlwaysPassDc = -20;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: CFEffectTestMob
  name: effect test mob
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      1000: Dead
  - type: Damageable
    damageContainer: Biological
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 200
  - type: CFAbilityScore
";

    /// <summary>Tiles the tests use, relative to the dev map origin.</summary>
    private static readonly Box2i Footprint = new(-3, -3, 16, 4);

    private CFDevMap _map = default!;

    /// <summary>Loads the dev map (<c>Resources/Prototypes/Maps/debug.yml</c>) and picks an open stretch of floor.</summary>
    private async Task<MapId> CreateFloor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        return _map.MapId;
    }

    /// <summary>Spawns a test creature in the center of grid tile (x, y).</summary>
    private EntityUid Mob(int x, int y)
    {
        return SEntMan.SpawnEntity("CFEffectTestMob", Local(x, y));
    }

    private int Hp(EntityUid uid)
    {
        return SEntMan.GetComponent<CFCombatantComponent>(uid).CurrentHp;
    }

    private EntityUid Spell(string prototype, int? fixedDc = null)
    {
        var action = SEntMan.SpawnEntity(prototype, MapCoordinates.Nullspace);
        if (fixedDc is { } dc)
        {
            var check = SEntMan.GetComponent<CFCheckComponent>(action);
            check.DcSource = CFDcSource.Fixed;
            check.FixedDc = dc;
        }

        return action;
    }

    private EntityCoordinates Local(int x, int y)
    {
        return _map.Local(x, y);
    }

    private MapCoordinates TileCenter(MapId map, int x, int y)
    {
        var coords = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(Local(x, y));
        Assert.That(coords.MapId, Is.EqualTo(map));
        return coords;
    }

    /// <summary>
    /// Force Barrage: one spell slot for the whole volley, one bolt per click, 1d4+1 force per bolt on arrival.
    /// </summary>
    [Test]
    public async Task ForceBarrageVolleySpendsOneSlot()
    {
        // The bolts need a clear line, which the area spells' big footprint cannot promise.
        _map = await CFDevMap.Load(Pair, new Box2i(-1, -1, 5, 2));
        EntityUid caster = default;
        EntityUid target = default;
        EntityUid spell = default;

        await Server.WaitPost(() =>
        {
            caster = Mob(0, 0);
            target = Mob(3, 0);
            var book = SEntMan.EnsureComponent<CFSpellbookComponent>(caster);
            book.Slots = new List<int> { 0, 1 };
            book.Expended = new List<int> { 0, 0 };
            spell = Spell("CFForceBarrage");

            var bolts = SEntMan.GetComponent<CFProjectileDeliveryComponent>(spell);
            bolts.ChosenBolts = 3;
            for (var shot = 0; shot < 3; shot++)
            {
                bolts.PayThisShot = shot == 0;
                var cast = new CFSpellCastTargetEvent { Performer = caster, Target = target };
                SEntMan.EventBus.RaiseLocalEvent(spell, cast);
                Assert.That(cast.Handled, $"Shot {shot + 1} was not handled.");
            }

            Assert.That(book.Expended[1], Is.EqualTo(1), "The volley must spend exactly one rank 1 slot.");
            Assert.That(bolts.VolleyLeft, Is.EqualTo(0));
            var homing = SEntMan.EntityQuery<CFHomingProjectileComponent>().Count(h => h.Target == target);
            Assert.That(homing, Is.EqualTo(3), "Each click fires one homing bolt.");
        });

        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            var taken = StartHp - Hp(target);
            Assert.That(taken, Is.InRange(3 * 2, 3 * 5), "Three bolts of 1d4+1 force.");
            Assert.That(Hp(caster), Is.EqualTo(StartHp));
        });
    }

    /// <summary>Breathe Fire: a 15-foot cone in front of the caster. Behind the caster is untouched.</summary>
    [Test]
    public async Task ConeHitsOnlyInFront()
    {
        var map = await CreateFloor();
        await Server.WaitAssertion(() =>
        {
            var caster = Mob(0, 0);
            var front = Mob(2, 0);
            var behind = Mob(-2, 0);
            var spell = Spell("CFBreatheFire", AlwaysFailDc);

            var effects = SEntMan.System<CFEffectSystem>();
            var count = effects.ResolveArea(spell, caster, TileCenter(map, 5, 0));

            Assert.That(count, Is.EqualTo(1));
            Assert.That(Hp(front), Is.LessThan(StartHp), "The creature in the cone fails its save.");
            Assert.That(Hp(behind), Is.EqualTo(StartHp));
            Assert.That(Hp(caster), Is.EqualTo(StartHp), "The caster's own square is never in its cone.");
        });
    }

    /// <summary>Fireball: a 20-foot burst at a point. The caster, far away, is not in it.</summary>
    [Test]
    public async Task BurstHitsAroundThePoint()
    {
        var map = await CreateFloor();
        await Server.WaitAssertion(() =>
        {
            var caster = Mob(0, 0);
            var center = Mob(8, 0);
            var edge = Mob(10, 2);
            var outside = Mob(14, 0);
            var spell = Spell("CFFireball", AlwaysFailDc);

            var effects = SEntMan.System<CFEffectSystem>();
            var aim = TileCenter(map, 8, 0);
            Assert.That(effects.ValidateArea(spell, caster, aim, out _));
            var count = effects.ResolveArea(spell, caster, aim);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(Hp(center), Is.LessThan(StartHp));
            Assert.That(Hp(edge), Is.LessThan(StartHp));
            Assert.That(Hp(outside), Is.EqualTo(StartHp));
            Assert.That(Hp(caster), Is.EqualTo(StartHp));
        });
    }

    /// <summary>Gale Blast: a 5-foot emanation around the caster that leaves the caster out.</summary>
    [Test]
    public async Task EmanationHitsAdjacentOnly()
    {
        var map = await CreateFloor();
        await Server.WaitAssertion(() =>
        {
            var caster = Mob(0, 0);
            var adjacent = Mob(1, 1);
            var far = Mob(3, 0);
            var spell = Spell("CFGaleBlast", AlwaysFailDc);

            var effects = SEntMan.System<CFEffectSystem>();
            var count = effects.ResolveArea(spell, caster, TileCenter(map, 1, 0));

            Assert.That(count, Is.EqualTo(1));
            Assert.That(Hp(adjacent), Is.LessThan(StartHp));
            Assert.That(Hp(far), Is.EqualTo(StartHp));
            Assert.That(Hp(caster), Is.EqualTo(StartHp));
        });
    }

    /// <summary>Fear: a failed Will save frightens; a critical success leaves the target alone.</summary>
    [Test]
    public async Task WillSaveAppliesConditionByDegree()
    {
        await CreateFloor();
        await Server.WaitAssertion(() =>
        {
            var caster = Mob(0, 0);
            var failer = Mob(2, 0);
            var passer = Mob(0, 2);
            var effects = SEntMan.System<CFEffectSystem>();
            var conditions = SEntMan.System<CFConditionSystem>();

            effects.DeliverToTarget(Spell("CFFear", AlwaysFailDc), caster, failer);
            effects.DeliverToTarget(Spell("CFFear", AlwaysPassDc), caster, passer);

            Assert.That(conditions.Frightened(failer), Is.InRange(1, 2), "Failure is frightened 1, critical failure 2.");
            Assert.That(conditions.Frightened(passer), Is.EqualTo(0));
        });
    }

    /// <summary>Heal: 1d8+8 on an ally, no check.</summary>
    [Test]
    public async Task HealRestoresHitPoints()
    {
        await CreateFloor();
        await Server.WaitAssertion(() =>
        {
            var caster = Mob(0, 0);
            var patient = Mob(1, 0);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(patient);
            combat.CurrentHp = 50;

            var spell = Spell("CFHeal");
            var effects = SEntMan.System<CFEffectSystem>();
            Assert.That(effects.CanTarget(spell, caster, patient));
            effects.DeliverToTarget(spell, caster, patient);

            Assert.That(Hp(patient), Is.InRange(50 + 9, 50 + 16));
        });
    }

    [Test]
    public void BonusesOfOneTypeDoNotStack()
    {
        var bonuses = new List<CFActiveBonus>
        {
            new() { Stat = CFBonusStat.ArmorClass, Type = CFBonusType.Status, Value = 1 },
            new() { Stat = CFBonusStat.ArmorClass, Type = CFBonusType.Status, Value = 2 },
            new() { Stat = CFBonusStat.ArmorClass, Type = CFBonusType.Circumstance, Value = 1 },
            new() { Stat = CFBonusStat.ArmorClass, Type = CFBonusType.Status, Value = -1 },
            new() { Stat = CFBonusStat.ArmorClass, Type = CFBonusType.Status, Value = -3 },
            new() { Stat = CFBonusStat.ArmorClass, Type = CFBonusType.Untyped, Value = -1 },
            new() { Stat = CFBonusStat.ArmorClass, Type = CFBonusType.Untyped, Value = -1 },
            new() { Stat = CFBonusStat.Saves, Type = CFBonusType.Status, Value = 5 },
            new() { Stat = CFBonusStat.WeaponDice, Type = CFBonusType.Status, Value = 1 },
            new() { Stat = CFBonusStat.WeaponDice, Type = CFBonusType.Item, Value = 2 },
        };

        // Status: best +2, worst -3. Circumstance +1. Untyped -1 -1.
        Assert.That(CFBonusSystem.Total(bonuses, CFBonusStat.ArmorClass), Is.EqualTo(2 - 3 + 1 - 2));
        Assert.That(CFBonusSystem.Total(bonuses, CFBonusStat.Saves), Is.EqualTo(5));
        Assert.That(CFBonusSystem.Total(bonuses, CFBonusStat.WeaponDice), Is.EqualTo(2));
        Assert.That(CFBonusSystem.Total(bonuses, CFBonusStat.AttackRoll), Is.EqualTo(0));
    }

    [Test]
    public void HeightenStepsFollowTheInterval()
    {
        // "1d4 at 3rd rank and every 2 ranks after" (cantrips).
        var cantrip = new CFHeightenComponent { BaseRank = 1, Interval = 2 };
        Assert.That(cantrip.Steps(1), Is.EqualTo(0));
        Assert.That(cantrip.Steps(2), Is.EqualTo(0));
        Assert.That(cantrip.Steps(3), Is.EqualTo(1));
        Assert.That(cantrip.Steps(5), Is.EqualTo(2));

        // "2d6 for each rank above 3rd" (fireball).
        var fireball = new CFHeightenComponent { BaseRank = 3 };
        Assert.That(fireball.Steps(3), Is.EqualTo(0));
        Assert.That(fireball.Steps(5), Is.EqualTo(2));

        Assert.That(CFCasting.CantripRank(1), Is.EqualTo(1));
        Assert.That(CFCasting.CantripRank(5), Is.EqualTo(3));
        Assert.That(CFCasting.CantripRank(6), Is.EqualTo(3));
    }
}
