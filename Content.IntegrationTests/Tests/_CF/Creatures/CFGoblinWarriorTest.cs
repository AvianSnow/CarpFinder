using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>
/// The Goblin Warrior on the goblin body: its weapons, its armor, and that it fights with its GOAP brain. Numbers are compared
/// with its own YAML, so it can be rebalanced.
/// </summary>
[TestFixture]
[TestOf(typeof(CFStrikeSystem))]
public sealed class CFGoblinWarriorTest : GameTest
{
    private const string Melee = "CFMobGoblinWarriorMelee";
    private const string Ranged = "CFMobGoblinWarriorRanged";

    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFGoblinWarriorTestHero
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

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private EntityUid Weapon(EntityUid mob)
    {
        return Hands.EnumerateHeld(mob).FirstOrDefault(held => SEntMan.HasComponent<CFWeaponComponent>(held));
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
            if (!ranged && printedAttack != null)
                Assert.That(attack, Is.EqualTo(printedAttack), "A melee Strike uses the printed attack.");
            if (!ranged && printedDamage != null)
                Assert.That(damage, Is.EqualTo(printedDamage), "A melee Strike uses the printed damage.");
        });
    }

    [Test]
    public async Task TheDogslicerIsAGoblinBlade()
    {
        await Server.WaitAssertion(() =>
        {
            var proto = Server.ProtoMan.Index(new ProtoId<EntityPrototype>("CFDogslicer"));
            Assert.That(proto.TryComp<CFTraitComponent>(out var traits, SEntMan.ComponentFactory));
            Assert.That(traits!.Traits.Select(t => t.Id),
                Is.SupersetOf(new[] { "Agile", "Backstabber", "Finesse", "Goblin" }),
                "Goblin Weapon Familiarity finds it by the Goblin trait.");
        });
    }

    /// <summary>The printed AC already counts the leather armor, so wearing it must not change AC, Speed or skills.</summary>
    [TestCase(Melee)]
    [TestCase(Ranged)]
    public async Task ItWearsLeatherArmorThatThePrintedAcAlreadyCounts(string id)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var mob = SEntMan.SpawnEntity(id, _map.Local(0, 0));
            var armor = SEntMan.System<CFArmorSystem>();
            var book = Server.ProtoMan.Index<EntityPrototype>(id);
            Assert.That(book.TryComp<CFCombatantComponent>(out var printed, SEntMan.ComponentFactory));

            Assert.Multiple(() =>
            {
                Assert.That(armor.TryGetWornArmor(mob, out _), "It wears its armor.");
                Assert.That(SEntMan.GetComponent<CFCombatantComponent>(mob).ArmorClass, Is.EqualTo(printed!.ArmorClass));
                Assert.That(armor.SpeedPenalty(mob), Is.Zero);
                Assert.That(armor.CheckPenalty(mob, "Acrobatics", CFAbility.Dexterity, false), Is.Zero);
            });
        });
    }

    /// <summary>The goblin body is a player species. Its bar is only 6 long, so nothing in the air may touch it.</summary>
    [Test]
    public async Task ItHoldsItsPostWithNobodyAround()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid mob = default;
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() =>
        {
            config.SetCVar(CCVars.NPCEnabled, false);
            mob = SEntMan.SpawnEntity(Melee, _map.Local(0, 0));
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

        Assert.That(dead, Is.False);
        Assert.That(hp, Is.EqualTo(maxHp));
    }

    [TestCase(Melee, new[] { "Close", "Strike" })]
    [TestCase(Ranged, new[] { "Shoot" })]
    public async Task ItPlansForItsWeapon(string id, string[] plan)
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var mob = SEntMan.SpawnEntity(id, _map.Local(0, 0));
            SEntMan.SpawnEntity("CFGoblinWarriorTestHero", _map.Local(4, 0));

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
    public async Task ItCutsWithTheDogslicer()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid mob = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity(Melee, _map.Local(0, 0));
            hero = SEntMan.SpawnEntity("CFGoblinWarriorTestHero", _map.Local(4, 0));
            Wake(mob, hero);
        });

        await RunNpcs(900);

        var hp = 0;
        await Server.WaitPost(() => hp = SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp);
        Assert.That(hp, Is.LessThan(1000));
    }

    [Test]
    public async Task ItStandsBackAndShoots()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid mob = default;
        EntityUid hero = default;
        var start = 0;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity(Ranged, _map.Local(0, 0));
            hero = SEntMan.SpawnEntity("CFGoblinWarriorTestHero", _map.Local(5, 0));
            start = SEntMan.GetComponent<CFArrowSupplyComponent>(mob).Arrows;
            Wake(mob, hero);
        });

        await RunNpcs(450);

        var arrows = 0;
        var hp = 0;
        var distance = 0f;
        await Server.WaitPost(() =>
        {
            arrows = SEntMan.GetComponent<CFArrowSupplyComponent>(mob).Arrows;
            hp = SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
            distance = Distance(mob, hero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(arrows, Is.LessThan(start), "It has been shooting.");
            Assert.That(hp, Is.LessThan(1000));
            Assert.That(distance, Is.GreaterThan(3f), "It shot from where it stood.");
        });
    }
}
