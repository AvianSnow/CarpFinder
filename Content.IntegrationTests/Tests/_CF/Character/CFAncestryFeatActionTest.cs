using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>
/// Actions from ancestry feats that need a fight around them: Absorb Strength, Frog Geyser, Leech-Clip, Left-Hand Blood,
/// Energized Font, Kobold Momentum and Spark Fist.
/// </summary>
[TestFixture]
[TestOf(typeof(CFEffectActionSystem))]
public sealed class CFAncestryFeatActionTest : GameTest
{
    private const int StartHp = 5000;

    private static readonly Box2i Footprint = new(-1, -1, 5, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    // Strength 40: every Strike hits, and nothing passes a save against the hero's class DC.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFFeatActionHero
  components:
  - type: CFCombatant
    thinkingTimer: 0
    level: 1
    armorClass: 1
    maxHp: 5000
    currentHp: 5000
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFFeatActionHero
  id: CFFeatActionFoe
  components:
  - type: CFCombatant
    level: 3
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private EntityUid Spawn(string id, int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Hero(string feat)
    {
        var hero = Spawn("CFFeatActionHero");
        var proto = Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(feat));
        SEntMan.System<CFCharacterFeatureSystem>().Apply(hero, proto.Grants, $"feat:{feat}");
        return hero;
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool CanUse(EntityUid hero, EntityUid action, EntityUid? target)
    {
        var attempt = new ActionAttemptEvent(hero, target);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        return !attempt.Cancelled;
    }

    private CFCombatantComponent Combat(EntityUid uid) => SEntMan.GetComponent<CFCombatantComponent>(uid);

    private CFCombatOptionsComponent Options(EntityUid uid) => SEntMan.GetComponent<CFCombatOptionsComponent>(uid);

    [Test]
    public async Task AbsorbStrengthNeedsADeadEnemy()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool living = true, dead = false;
        var temp = 0;
        await Server.WaitPost(() =>
        {
            var kholo = Hero("CFAbsorbStrength");
            var action = Action(kholo, "CFActAbsorbStrength");
            var foe = Spawn("CFFeatActionFoe", 1);
            living = CanUse(kholo, action, foe);

            SEntMan.System<MobStateSystem>().ChangeMobState(foe, MobState.Dead);
            dead = CanUse(kholo, action, foe);
            Actions.PerformAction(kholo, action, new CFActivityTargetEvent { Target = foe });
            temp = Combat(kholo).TemporaryHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(living, Is.False);
            Assert.That(dead, Is.True);
            Assert.That(temp, Is.EqualTo(3), "The dead enemy's level.");
        });
    }

    [Test]
    public async Task EnergizedFontGivesAFocusPointBackOnceADay()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int spent = -1;
        var again = true;
        await Server.WaitPost(() =>
        {
            var gnome = Hero("CFEnergizedFont");
            var book = SEntMan.EnsureComponent<CFSpellbookComponent>(gnome);
            book.FocusPoints = 2;
            book.FocusSpent = 2;
            var action = Action(gnome, "CFActEnergizedFont");
            Actions.PerformAction(gnome, action, new CFActivityInstantEvent());
            spent = book.FocusSpent;
            again = CanUse(gnome, action, null);
        });

        Assert.Multiple(() =>
        {
            Assert.That(spent, Is.EqualTo(1));
            Assert.That(again, Is.False, "Once a day.");
        });
    }

    [Test]
    public async Task LeftHandBloodPoisonsAHeldWeaponsNextHit()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool unarmed = true, armed = false, poisoned = false;
        var cost = 0;
        await Server.WaitPost(() =>
        {
            var kholo = Hero("CFLeftHandBlood");
            var action = Action(kholo, "CFActLeftHandBlood");
            unarmed = CanUse(kholo, action, null);

            var sword = Spawn("CFLongsword");
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(kholo, sword);
            armed = CanUse(kholo, action, null);
            Actions.PerformAction(kholo, action, new CFActivityInstantEvent());
            cost = StartHp - Combat(kholo).CurrentHp;

            var foe = Spawn("CFFeatActionFoe", 1);
            SEntMan.System<CFWeaponSystem>().TryStats(sword, out var stats);
            SEntMan.System<CFStrikeSystem>().PerformStrike(kholo, foe, stats, sword, melee: true, countMap: false, fixedMap: 0);
            poisoned = SEntMan.TryGetComponent(foe, out CFPersistentDamageComponent persistent)
                       && persistent.Entries.Any(e => e.DamageType.Id == "Poison" && e.Die == 4);
        });

        Assert.Multiple(() =>
        {
            Assert.That(unarmed, Is.False, "It needs a weapon in hand.");
            Assert.That(armed, Is.True);
            Assert.That(cost, Is.EqualTo(1), "1 slashing damage to yourself.");
            Assert.That(poisoned, Is.True, "1d4 persistent poison on the next hit.");
        });
    }

    [Test]
    public async Task SparkFistBurnsBothAndKoboldMomentumIsGranted()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, burned = 0;
        var momentum = false;
        await Server.WaitPost(() =>
        {
            var strikes = SEntMan.System<CFStrikeSystem>();
            var dwarf = Hero("CFSparkFist");
            var foe = Spawn("CFFeatActionFoe", 1);
            strikes.PerformStrike(dwarf, foe, strikes.UnarmedStats(dwarf), dwarf, melee: true, countMap: false, fixedMap: 0);
            plain = StartHp - Combat(dwarf).CurrentHp;

            Actions.PerformAction(dwarf, Action(dwarf, "CFActSparkFist"), new CFActivityInstantEvent());
            strikes.PerformStrike(dwarf, foe, strikes.UnarmedStats(dwarf), dwarf, melee: true, countMap: false, fixedMap: 0);
            burned = StartHp - Combat(dwarf).CurrentHp;

            momentum = SEntMan.HasComponent<CFDraconicMomentumComponent>(Hero("CFKoboldMomentum"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.Zero);
            Assert.That(burned, Is.EqualTo(1), "The fist burns its owner 1 fire per hit.");
            Assert.That(momentum, Is.True, "A critical hit recharges the breath, as the dragon's Draconic Momentum.");
        });
    }

    [Test]
    public async Task LeechClipNeedsAFlailAndSlows()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string without = null;
        var speed = 0;
        await Server.WaitPost(() =>
        {
            var hobgoblin = Hero("CFLeechClip");
            var action = Action(hobgoblin, "CFActLeechClip");
            var foe = Spawn("CFFeatActionFoe", 1);
            var hands = SEntMan.System<SharedHandsSystem>();

            hands.TryPickupAnyHand(hobgoblin, Spawn("CFLongsword"));
            Actions.PerformAction(hobgoblin, action, new CFActivityTargetEvent { Target = foe });
            without = SEntMan.System<CFBonusSystem>().Total(foe, CFBonusStat.Speed) == 0 ? null : "slowed";

            hands.TryDrop(hobgoblin);
            hands.TryPickupAnyHand(hobgoblin, Spawn("CFFlail"));
            Actions.PerformAction(hobgoblin, action, new CFActivityTargetEvent { Target = foe });
            speed = SEntMan.System<CFBonusSystem>().Total(foe, CFBonusStat.Speed);
        });

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.Null, "A sword is not a flail.");
            Assert.That(speed, Is.AnyOf(-10, -15), "Slowed, more on a critical hit.");
        });
    }

    [Test]
    public async Task FrogGeyserPushesWhoeverIsInTheLine()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid hero = default, foe = default;
        var breathed = false;
        await Server.WaitPost(() =>
        {
            hero = Hero("CFFrogGeyser");
            foe = Spawn("CFFeatActionFoe", 1);
            var xform = SEntMan.System<SharedTransformSystem>();
            breathed = SEntMan.System<CFBreathWeaponSystem>().TryBreathe(hero, Action(hero, "CFActFrogGeyser"), xform.GetMapCoordinates(foe));
        });
        await Pair.RunSeconds(1f);

        var distance = 0f;
        await Server.WaitPost(() =>
        {
            var xform = SEntMan.System<SharedTransformSystem>();
            distance = (xform.GetWorldPosition(foe) - xform.GetWorldPosition(hero)).Length();
        });

        Assert.Multiple(() =>
        {
            Assert.That(breathed, Is.True);
            Assert.That(distance, Is.GreaterThan(1.2f), "Pushed back from 1 tile away.");
        });
    }
}
