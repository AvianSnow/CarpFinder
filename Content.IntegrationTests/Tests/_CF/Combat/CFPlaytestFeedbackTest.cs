using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.ActionBlocker;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Medicine;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Action pool speed, NPC thinking time, melee aim assist, magic effects and the healer's toolkit.</summary>
[TestFixture]
public sealed class CFPlaytestFeedbackTest : GameTest
{
    private const float Reach = 1.5f;

    private static readonly Box2i Footprint = new(-3, -2, 6, 3);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFFeedbackHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFFeedbackHero
  id: CFFeedbackFoe
  components:
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFFeedbackHero
  id: CFFeedbackBystander
  components:
  - type: CFFaction
    faction: Neutral

- type: entity
  parent: CFFeedbackFoe
  id: CFFeedbackBoss
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 100
    thinkingTimer: 0.2

- type: entity
  id: CFFeedbackToolkit
  components:
  - type: CFHealersToolkit
";

    private CFDevMap _map = default!;

    private CFMeleeAimSystem Aim => SEntMan.System<CFMeleeAimSystem>();
    private ActionBlockerSystem Blocker => SEntMan.System<ActionBlockerSystem>();
    private CFEffectSystem Effects => SEntMan.System<CFEffectSystem>();
    private CFMedicineSystem Medicine => SEntMan.System<CFMedicineSystem>();
    private CFVfxSystem Vfx => SEntMan.System<CFVfxSystem>();

    private EntityUid Spawn(string id, int x, int y = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, y));
    }

    private MapCoordinates AimFrom(EntityUid hero, float x, float y = 0f)
    {
        var at = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);
        return new MapCoordinates(at.Position + new Vector2(x, y), at.MapId);
    }

    /// <summary>The effects of one kind attached to a creature, as their colours.</summary>
    private List<Color> Effect(EntityUid creature, string prototype)
    {
        var found = new List<Color>();
        var query = SEntMan.AllEntityQueryEnumerator<CFVfxComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out var vfx, out var xform, out var meta))
        {
            if (xform.ParentUid == creature && meta.EntityPrototype?.ID == prototype)
                found.Add(vfx.Color);
        }

        return found;
    }

    [Test]
    public async Task AnNpcRegainsActionsAtItsOwnSpeed()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFActionPoolComponent pool = default!;
        float early = 0f, late = 0f;
        await Server.WaitPost(() =>
        {
            pool = SEntMan.GetComponent<CFActionPoolComponent>(Spawn("CFFeedbackFoe", 0));
            pool.Stamina = 0f;
        });
        await Pair.RunSeconds(1.3f);
        await Server.WaitPost(() => early = pool.Stamina);
        await Pair.RunSeconds(0.4f);
        await Server.WaitPost(() => late = pool.Stamina);

        Assert.Multiple(() =>
        {
            Assert.That(pool.RegenSeconds, Is.EqualTo(1.2f), "What a player gets.");
            Assert.That(early, Is.EqualTo(0f));
            Assert.That(late, Is.EqualTo(1f));
        });
    }

    [Test]
    public async Task AnNpcThinksBeforeEachAttackOnACreature()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid mob = default, target = default, door = default;
        bool first = true, during = true, onDoor = false, after = false;
        await Server.WaitPost(() =>
        {
            mob = Spawn("CFFeedbackFoe", 0);
            target = Spawn("CFFeedbackHero", 1);
            door = SEntMan.SpawnEntity(null, _map.Local(1, 1));
            first = Blocker.CanAttack(mob, target);
            onDoor = Blocker.CanAttack(mob, door);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() => during = Blocker.CanAttack(mob, target));
        await Pair.RunSeconds(0.7f);
        await Server.WaitPost(() => after = Blocker.CanAttack(mob, target));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False, "The first try starts the timer.");
            Assert.That(during, Is.False);
            Assert.That(after, Is.True);
            Assert.That(onDoor, Is.True, "Obstacles need no thought.");
        });
    }

    [Test]
    public async Task AQuickCreatureThinksFaster()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid boss = default, target = default;
        var ready = false;
        await Server.WaitPost(() =>
        {
            boss = Spawn("CFFeedbackBoss", 0);
            target = Spawn("CFFeedbackHero", 1);
            Blocker.CanAttack(boss, target);
        });
        await Pair.RunSeconds(0.3f);
        await Server.WaitPost(() => ready = Blocker.CanAttack(boss, target));

        Assert.That(ready, Is.True);
    }

    [Test]
    public async Task ASwingPicksTheClosestEnemyInTheAimedDirection()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, near = default, picked = default, pickedDiagonal = default;
        bool found = false, behind = true, diagonal = false;
        await Server.WaitPost(() =>
        {
            hero = Spawn("CFFeedbackHero", 0);
            near = Spawn("CFFeedbackFoe", 1);
            Spawn("CFFeedbackFoe", 2);

            found = Aim.TryPick(hero, null, AimFrom(hero, 5f), Reach, out picked);
            behind = Aim.TryPick(hero, null, AimFrom(hero, -5f), Reach, out _);
            diagonal = Aim.TryPick(hero, null, AimFrom(hero, 2f, 1.5f), Reach, out pickedDiagonal);
        });

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True, "The cursor can be well past the weapon's reach.");
            Assert.That(picked, Is.EqualTo(near));
            Assert.That(behind, Is.False);
            Assert.That(diagonal, Is.True, "It does not have to be aimed exactly.");
            Assert.That(pickedDiagonal, Is.EqualTo(near));
        });
    }

    [Test]
    public async Task AimAssistLeavesAlone()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool ally = true, bystander = true, clicked = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn("CFFeedbackHero", 0);
            Spawn("CFFeedbackHero", 1);
            Spawn("CFFeedbackBystander", 1, 1);
            ally = Aim.TryPick(hero, null, AimFrom(hero, 5f), Reach, out _);
            bystander = Aim.TryPick(hero, null, AimFrom(hero, 1f, 1f), Reach, out _);

            var foe = Spawn("CFFeedbackFoe", 0, 1);
            clicked = Aim.TryPick(hero, foe, AimFrom(hero, 0f, 1f), Reach, out _);
        });

        Assert.Multiple(() =>
        {
            Assert.That(ally, Is.False);
            Assert.That(bystander, Is.False, "A neutral creature has to be clicked.");
            Assert.That(clicked, Is.False, "A click on an enemy in reach already hits it.");
        });
    }

    [Test]
    public async Task HealingSparklesOnTheHealerAndTheHealed()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid healer = default, patient = default;
        await Server.WaitPost(() =>
        {
            healer = Spawn("CFFeedbackHero", 0);
            patient = Spawn("CFFeedbackHero", 1);
            Effects.Heal(healer, patient, 5);
        });
        await Pair.RunTicksSync(1);

        await Server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(Effect(patient, "CFVfxHealing"), Is.EqualTo(new[] { CFVfxSystem.HealingColor }));
            Assert.That(Effect(healer, "CFVfxHealing"), Has.Count.EqualTo(1));
        }));
    }

    [Test]
    public async Task ASaveSpellSparklesInItsDamageColour()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid target = default;
        await Server.WaitPost(() =>
        {
            var caster = Spawn("CFFeedbackHero", 0);
            target = Spawn("CFFeedbackFoe", 1);
            Effects.ResolveTarget(SEntMan.SpawnEntity("CFDaze", MapCoordinates.Nullspace), caster, target);
        });
        await Pair.RunTicksSync(1);

        await Server.WaitAssertion(() =>
        {
            var mental = Server.ProtoMan.Index(new ProtoId<CFDamageTypePrototype>("Mental")).Color;
            Assert.That(Effect(target, "CFVfxTargeted"), Is.EqualTo(new[] { mental }));
        });
    }

    private async Task<(EntityUid Healer, EntityUid Patient, CFActionPoolComponent Pool)> StartTreatment()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid healer = default, patient = default;
        CFActionPoolComponent pool = default!;
        await Server.WaitPost(() =>
        {
            healer = Spawn("CFFeedbackHero", 0);
            patient = Spawn("CFFeedbackHero", 1);
            pool = SEntMan.GetComponent<CFActionPoolComponent>(healer);
            pool.NpcRegenSeconds = 100000f;

            // A patient with no player tries to Escape by itself, and this test decides when that works.
            SEntMan.System<CFConditionSystem>().Ensure(patient).NextEscape = TimeSpan.MaxValue;

            var toolkit = SEntMan.SpawnEntity("CFFeedbackToolkit", _map.Local(0, 0));
            Assert.That(Medicine.TryStartTreatment(toolkit, healer, patient), Is.True);
        });
        return (healer, patient, pool);
    }

    [Test]
    public async Task TreatmentHoldsThePatientAndCostsThreeActionsOverTwelveSeconds()
    {
        var (healer, patient, pool) = await StartTreatment();
        var condition = SEntMan.GetComponent<CFConditionComponent>(patient);
        float atStart = pool.Stamina, afterOne = 0f, afterTwo = 0f;
        var held = condition.Grabbed;

        await Pair.RunSeconds(4.5f);
        await Server.WaitPost(() => afterOne = pool.Stamina);
        await Pair.RunSeconds(4f);
        await Server.WaitPost(() => afterTwo = pool.Stamina);
        await Pair.RunSeconds(4f);

        await Server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(held, Is.True);
            Assert.That(atStart, Is.EqualTo(2f));
            Assert.That(afterOne, Is.EqualTo(1f));
            Assert.That(afterTwo, Is.EqualTo(0f));
            Assert.That(condition.Grabbed, Is.False, "Finishing lets go.");
            Assert.That(SEntMan.HasComponent<CFTreatingComponent>(healer), Is.False);
            Assert.That(SEntMan.GetComponent<CFPatientComponent>(patient).TreatWoundsUntil, Is.GreaterThan(TimeSpan.Zero));
        }));
    }

    [Test]
    public async Task ThePatientBreakingFreeEndsTheTreatment()
    {
        var (healer, patient, _) = await StartTreatment();
        await Server.WaitPost(() => SEntMan.System<CFConditionSystem>().ReleaseGrab(patient));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(SEntMan.HasComponent<CFTreatingComponent>(healer), Is.False);
            Assert.That(SEntMan.HasComponent<CFPatientComponent>(patient), Is.False, "It never finished.");
        }));
    }

    [Test]
    public async Task RunningOutOfActionsEndsTheTreatment()
    {
        var (healer, _, pool) = await StartTreatment();
        await Server.WaitPost(() => pool.Stamina = 0f);
        await Pair.RunSeconds(4.5f);

        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<CFTreatingComponent>(healer), Is.False));
    }
}
