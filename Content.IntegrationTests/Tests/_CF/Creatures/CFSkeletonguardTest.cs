using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.CF.GOAP.Encounters;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>The Skeleton Guard: its stat block, and that it fights with its GOAP brain.</summary>
[TestFixture]
[TestOf(typeof(CFArrowSupplySystem))]
public sealed class CFSkeletonguardTest : GameTest
{
    private const string Melee = "CFMobSkeletonguardMelee";
    private const string Ranged = "CFMobSkeletonguardRanged";

    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSkeletonguardTestHero
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

# Fixed numbers, so rebalancing the real skeleton cannot change what the resistance test measures.
- type: entity
  parent: CFMobSkeletonguardMelee
  id: CFSkeletonguardTestBones
  components:
  - type: CFCombatant
    maxHp: 4
    currentHp: 4
    diesAtZeroHp: true
  - type: CFResistances
    entries:
    - damageType: Fire
      amount: 5
    - damageType: Piercing
      amount: 5
    - damageType: Slashing
      amount: 5
    - damageType: Mental
      amount: 9999
    - damageType: Void
      amount: 9999
";

    private CFDevMap _map = default!;

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private EntityUid Weapon(EntityUid mob)
    {
        return Hands.EnumerateHeld(mob).FirstOrDefault(held => SEntMan.HasComponent<CFWeaponComponent>(held));
    }

    private string WeaponId(EntityUid mob)
    {
        return SEntMan.GetComponent<MetaDataComponent>(Weapon(mob)).EntityPrototype?.ID;
    }

    private int ArrowsOnBow(EntityUid mob)
    {
        var count = new GetAmmoCountEvent();
        SEntMan.EventBus.RaiseLocalEvent(Weapon(mob), ref count);
        return count.Count;
    }

    private float Distance(EntityUid a, EntityUid b)
    {
        var transform = SEntMan.System<SharedTransformSystem>();
        return (transform.GetWorldPosition(a) - transform.GetWorldPosition(b)).Length();
    }

    private void Wake(params EntityUid[] mobs)
    {
        foreach (var mob in mobs)
        {
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
        }

        SEntMan.System<NPCSystem>().WakeNPC(mobs[0]);
    }

    /// <summary>Runs NPCs for a while. Asserts go on the test thread after this, where a failure keeps its message.</summary>
    private async Task RunNpcs(int ticks)
    {
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() => config.SetCVar(CCVars.NPCEnabled, true));
        await Pair.RunTicksSync(ticks);
        await Server.WaitPost(() => config.SetCVar(CCVars.NPCEnabled, false));
    }

    /// <summary>Which weapon it carries and how hard it hits are balance, so this only checks that the weapon works.</summary>
    [TestCase(Melee, false)]
    [TestCase(Ranged, true)]
    public async Task ItsWeaponWorks(string id, bool ranged)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hasWeapon = false;
        var hasStats = false;
        CFStrikeWeaponStats stats = default;
        int? printedAttack = null;
        int? printedDamage = null;
        var attack = 0;
        var damage = 0;
        await Server.WaitPost(() =>
        {
            var mob = SEntMan.SpawnEntity(id, _map.Local(0, 0));
            var weapon = Weapon(mob);
            hasWeapon = weapon.IsValid();
            if (!hasWeapon)
                return;

            hasStats = SEntMan.System<CFWeaponSystem>().TryStats(weapon, out stats);
            var combat = SEntMan.GetComponent<CFCombatantComponent>(mob);
            var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(mob);
            printedAttack = combat.PrintedAttack;
            printedDamage = combat.PrintedDamage;
            attack = CFMelee.AttackBonus(abilities, combat, stats, CFProficiencyRank.Trained);
            damage = CFMelee.DamageBonus(abilities, stats, combat);
        });

        Assert.That(hasWeapon, "It spawns holding a weapon.");
        Assert.That(hasStats, "Its weapon has Strike stats.");
        Assert.Multiple(() =>
        {
            Assert.That(stats.Ranged, Is.EqualTo(ranged));
            Assert.That(stats.DamageDice, Is.GreaterThan(0));
            Assert.That(stats.DamageDie, Is.GreaterThan(0));
            if (!ranged && printedAttack != null)
                Assert.That(attack, Is.EqualTo(printedAttack), "A melee Strike uses the printed attack.");
            if (!ranged && printedDamage != null)
                Assert.That(damage, Is.EqualTo(printedDamage), "A melee Strike uses the printed damage.");
        });
    }

    [Test]
    public async Task BonesShrugOffBladesAndBreakToBlunts()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = SEntMan.SpawnEntity("CFSkeletonguardTestBones", _map.Local(0, 0));
            var combat = SEntMan.GetComponent<CFCombatantComponent>(mob);
            var effects = SEntMan.System<CFEffectSystem>();

            effects.DealDamage(mob, mob, 5, "Slashing", false);
            effects.DealDamage(mob, mob, 5, "Piercing", false);
            effects.DealDamage(mob, mob, 5, "Fire", false);
            effects.DealDamage(mob, mob, 100, "Mental", false);
            effects.DealDamage(mob, mob, 100, "Void", false);
            Assert.That(combat.CurrentHp, Is.EqualTo(4), "All of that is resisted or immune.");

            effects.DealDamage(mob, mob, 7, "Piercing", false);
            Assert.That(combat.CurrentHp, Is.EqualTo(2));

            effects.DealDamage(mob, mob, 2, "Bludgeoning", false);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(mob), "It dies at 0 HP, on a bar only 4 long.");
        });
    }

    [Test]
    public async Task MindlessBonesCannotBeFrightenedOrPutToSleep()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = SEntMan.SpawnEntity(Melee, _map.Local(0, 0));
            var hero = SEntMan.SpawnEntity("CFSkeletonguardTestHero", _map.Local(1, 0));
            var conditions = SEntMan.System<CFConditionSystem>();

            conditions.ApplyCondition(mob, CFConditionKind.Frightened, 2, 6f, hero, 0);
            conditions.ApplyCondition(mob, CFConditionKind.Asleep, 0, 6f, hero, 0);
            conditions.ApplyCondition(mob, CFConditionKind.Paralyzed, 0, 6f, hero, 0);

            Assert.Multiple(() =>
            {
                Assert.That(conditions.Frightened(mob), Is.Zero);
                Assert.That(conditions.IsProne(mob), Is.False);
                Assert.That(conditions.CanDemoralize(hero, mob), Is.False);
                Assert.That(conditions.TryDemoralize(hero, mob), Is.False);
            });

            conditions.ApplyCondition(mob, CFConditionKind.Prone, 0, 6f, hero, 0);
            Assert.That(conditions.IsProne(mob), "Only what the stat block names is ruled out.");
        });
    }

    [Test]
    public async Task VitalityDoesNotHealIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = SEntMan.SpawnEntity(Melee, _map.Local(0, 0));
            var combat = SEntMan.GetComponent<CFCombatantComponent>(mob);
            combat.CurrentHp = 1;

            SEntMan.System<CFEffectSystem>().Heal(mob, mob, 3);
            Assert.That(combat.CurrentHp, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ItHoldsItsPostWithNobodyAround()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid mob = default;
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, false);
            mob = SEntMan.SpawnEntity(Ranged, _map.Local(0, 0));
        });

        await Pair.RunTicksSync(1800);

        var hp = 0;
        var maxHp = -1;
        var dead = true;
        await Server.WaitPost(() =>
        {
            var combat = SEntMan.GetComponent<CFCombatantComponent>(mob);
            hp = combat.CurrentHp;
            maxHp = combat.MaxHp;
            dead = SEntMan.System<MobStateSystem>().IsDead(mob);
        });

        Assert.That(dead, Is.False, "Nothing in the air should kill a skeleton.");
        Assert.That(hp, Is.EqualTo(maxHp), "Nothing in the air should hurt one either.");
    }

    [Test]
    public async Task ItCutsWithTheScimitar()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid mob = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity(Melee, _map.Local(0, 0));
            hero = SEntMan.SpawnEntity("CFSkeletonguardTestHero", _map.Local(4, 0));
            Wake(mob, hero);
        });

        await RunNpcs(900);

        var hp = 0;
        await Server.WaitPost(() => hp = SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp);
        Assert.That(hp, Is.LessThan(1000));
    }

    [Test]
    public async Task ItStandsBackAndShootsThroughItsArrows()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid mob = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity(Ranged, _map.Local(0, 0));
            hero = SEntMan.SpawnEntity("CFSkeletonguardTestHero", _map.Local(5, 0));
            SEntMan.GetComponent<CFArrowSupplyComponent>(mob).Arrows = 19;
            Wake(mob, hero);
        });

        await RunNpcs(450);

        var arrows = 0;
        var nocked = 0;
        var hp = 0;
        var distance = 0f;
        await Server.WaitPost(() =>
        {
            arrows = SEntMan.GetComponent<CFArrowSupplyComponent>(mob).Arrows;
            nocked = ArrowsOnBow(mob);
            hp = SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
            distance = Distance(mob, hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(arrows, Is.LessThan(19), "It has been shooting.");
            Assert.That(nocked, Is.EqualTo(1), "The next arrow is already on the bow.");
            Assert.That(hp, Is.LessThan(1000));
            Assert.That(distance, Is.GreaterThan(3f), "It shot from where it stood.");
        });
    }

    /// <summary>Its brain keeps the empty bow in hand and closes in to claw.</summary>
    [Test]
    public async Task OutOfArrowsItUsesItsClaws()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid mob = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity(Ranged, _map.Local(0, 0));
            hero = SEntMan.SpawnEntity("CFSkeletonguardTestHero", _map.Local(3, 0));
            SEntMan.GetComponent<CFArrowSupplyComponent>(mob).Arrows = 0;
            Wake(mob, hero);
        });

        await RunNpcs(900);

        var hp = 0;
        var distance = 0f;
        await Server.WaitPost(() =>
        {
            hp = SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
            distance = Distance(mob, hero);
        });

        Assert.That(distance, Is.LessThan(2f), "It walked up to use its claws.");
        Assert.That(hp, Is.LessThan(1000), "It hurt them.");
    }

    [TestCase(Melee, new[] { "Close", "Strike" })]
    [TestCase(Ranged, new[] { "Shoot" })]
    public async Task ItPlansForItsWeapon(string id, string[] plan)
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var mob = SEntMan.SpawnEntity(id, _map.Local(0, 0));
            SEntMan.SpawnEntity("CFSkeletonguardTestHero", _map.Local(4, 0));

            var goap = SEntMan.GetComponent<CFGOAPComponent>(mob);
            var system = SEntMan.System<CFGOAPSystem>();
            system.Start((mob, goap));
            system.Think((mob, goap));

            var chosen = new List<string>();
            foreach (var step in goap.Plan)
            {
                chosen.Add(step);
            }

            Assert.That(chosen, Is.EqualTo(plan));
        });
    }

    [Test]
    public async Task AnEncounterCanEnlistBothVariants()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("CFEncounterSpawner", _map.Local(0, 0));
            var spawner = SEntMan.GetComponent<CFGOAPEncounterSpawnerComponent>(uid);
            spawner.Mobs = $"{Melee},{Ranged}";
            spawner.Count = 2;

            SEntMan.System<CFGOAPEncounterSystem>().Spawn((uid, spawner));

            Assert.That(spawner.Members, Has.Count.EqualTo(2));
            foreach (var member in spawner.Members)
            {
                Assert.That(SEntMan.HasComponent<CFGOAPComponent>(member));
                Assert.That(Weapon(member).IsValid(), "It still has its weapon.");
            }
        });
    }
}
