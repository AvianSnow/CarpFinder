using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.GOAP;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Creatures;

/// <summary>
/// The Hobgoblin Soldier: its gear, raising its shield between swings, and the archer drawing its sword. Numbers are
/// compared with its own YAML, so it can be rebalanced.
/// </summary>
[TestFixture]
[TestOf(typeof(CFShieldSystem))]
public sealed class CFHobgoblinSoldierTest : GameTest
{
    private const string Melee = "CFMobHobgoblinSoldierMelee";
    private const string Ranged = "CFMobHobgoblinSoldierRanged";

    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    // AC 18, so a third swing at +8 is not worth it, as in CFGOAPTest.ItWeighsAThirdSwing.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFHobgoblinSoldierTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 18
    maxHp: 1000
    currentHp: 1000
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha
";

    private CFDevMap _map = default!;

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private string? Id(EntityUid? uid)
    {
        return uid == null ? null : SEntMan.GetComponent<MetaDataComponent>(uid.Value).EntityPrototype?.ID;
    }

    private EntityUid? Active(EntityUid mob)
    {
        return Hands.TryGetActiveItem(mob, out var item) ? item : null;
    }

    private void Wake(params EntityUid[] mobs)
    {
        foreach (var mob in mobs)
        {
            SEntMan.System<SharedPhysicsSystem>().WakeBody(mob);
        }

        SEntMan.System<NPCSystem>().WakeNPC(mobs[0]);
    }

    private async Task RunNpcs(int ticks)
    {
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() => config.SetCVar(CCVars.NPCEnabled, true));
        await Pair.RunTicksSync(ticks);
        await Server.WaitPost(() => config.SetCVar(CCVars.NPCEnabled, false));
    }

    private List<string> Plan(Entity<CFGOAPComponent> mob)
    {
        var system = SEntMan.System<CFGOAPSystem>();
        system.Think(mob);
        return mob.Comp.Plan.Select(step => step.Id).ToList();
    }

    /// <summary>The sword is in the active hand, so a swing is a longsword Strike and not a shield bash.</summary>
    [Test]
    public async Task ItSwingsTheSwordWithTheShieldInTheOtherHand()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        string? active = null;
        var holdsShield = false;
        int? printedAttack = null;
        int? printedDamage = null;
        var attack = 0;
        var damage = 0;
        await Server.WaitPost(() =>
        {
            var mob = SEntMan.SpawnEntity(Melee, _map.Local(0, 0));
            var sword = Active(mob);
            active = Id(sword);
            holdsShield = SEntMan.System<CFShieldSystem>().TryGetUsableShield(mob, out _);
            if (sword == null || !SEntMan.System<CFWeaponSystem>().TryStats(sword.Value, out var stats))
                return;

            var combat = SEntMan.GetComponent<CFCombatantComponent>(mob);
            var abilities = SEntMan.GetComponent<CFAbilityScoreComponent>(mob);
            printedAttack = combat.PrintedAttack;
            printedDamage = combat.PrintedDamage;
            attack = CFMelee.AttackBonus(abilities, combat, stats, CFProficiencyRank.Trained);
            damage = CFMelee.DamageBonus(abilities, stats, combat);
        });

        Assert.Multiple(() =>
        {
            Assert.That(active, Is.EqualTo("CFLongsword"));
            Assert.That(holdsShield, "The shield is in the other hand.");
            Assert.That(attack, Is.EqualTo(printedAttack), "A melee Strike uses the printed attack.");
            Assert.That(damage, Is.EqualTo(printedDamage), "A melee Strike uses the printed damage.");
        });
    }

    /// <summary>The printed AC already counts the breastplate, and Strength +3 meets it.</summary>
    [TestCase(Melee)]
    [TestCase(Ranged)]
    public async Task ItsBreastplateCostsItNothing(string id)
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
                Assert.That(armor.CheckPenalty(mob, "Athletics", CFAbility.Strength, false), Is.Zero);
            });
        });
    }

    [Test]
    public async Task TheArcherPlansToShoot()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity(Ranged, _map.Local(0, 0));
            SEntMan.SpawnEntity("CFHobgoblinSoldierTestHero", _map.Local(4, 0));
            var mob = new Entity<CFGOAPComponent>(uid, SEntMan.GetComponent<CFGOAPComponent>(uid));
            SEntMan.System<CFGOAPSystem>().Start(mob);

            Assert.That(Plan(mob), Is.EqualTo(new[] { "Shoot" }));
        });
    }

    /// <summary>A raised shield stays up, so it raises it once on seeing an enemy, then closes in behind it.</summary>
    [Test]
    public async Task ItRaisesItsShieldOnSightThenCloses()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity(Melee, _map.Local(0, 0));
            SEntMan.SpawnEntity("CFHobgoblinSoldierTestHero", _map.Local(4, 0));
            var mob = new Entity<CFGOAPComponent>(uid, SEntMan.GetComponent<CFGOAPComponent>(uid));
            SEntMan.System<CFGOAPSystem>().Start(mob);

            Assert.That(Plan(mob), Is.EqualTo(new[] { "RaiseShield" }));
            Assert.That(mob.Comp.Goal?.Id, Is.EqualTo("RaiseShield"));

            var shields = SEntMan.System<CFShieldSystem>();
            Assert.That(shields.TryGetUsableShield(uid, out var shield));
            shields.SetRaised(shield, true);
            Assert.That(Plan(mob), Is.EqualTo(new[] { "Close", "Strike" }), "A raised shield is not raised again.");
        });
    }

    /// <summary>With no action to spare it does not stand and wait to raise it.</summary>
    [Test]
    public async Task WithNoActionsLeftItFightsOn()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity(Melee, _map.Local(0, 0));
            SEntMan.SpawnEntity("CFHobgoblinSoldierTestHero", _map.Local(4, 0));
            var mob = new Entity<CFGOAPComponent>(uid, SEntMan.GetComponent<CFGOAPComponent>(uid));
            SEntMan.System<CFGOAPSystem>().Start(mob);
            SEntMan.EnsureComponent<CFActionPoolComponent>(uid).Stamina = 0f;

            Assert.That(Plan(mob), Does.Not.Contain("RaiseShield"));
        });
    }

    [Test]
    public async Task ItRaisesItsShieldInAFight()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid mob = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity(Melee, _map.Local(0, 0));
            hero = SEntMan.SpawnEntity("CFHobgoblinSoldierTestHero", _map.Local(2, 0));
            Wake(mob, hero);
        });

        await RunNpcs(900);

        var raised = false;
        var bonus = 0;
        var hp = 0;
        await Server.WaitPost(() =>
        {
            var shields = SEntMan.System<CFShieldSystem>();
            raised = shields.TryGetRaisedShield(mob, out _);
            bonus = shields.RaisedBonus(mob);
            hp = SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp;
        });

        Assert.Multiple(() =>
        {
            Assert.That(hp, Is.LessThan(1000), "It fought.");
            Assert.That(raised, "It raised its shield and kept it up.");
            Assert.That(bonus, Is.GreaterThan(0));
        });
    }

    /// <summary>With its last arrow gone it drops the bow and draws the longsword from its neck.</summary>
    [Test]
    public async Task OutOfArrowsTheArcherDrawsItsSword()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        EntityUid mob = default;
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity(Ranged, _map.Local(0, 0));
            hero = SEntMan.SpawnEntity("CFHobgoblinSoldierTestHero", _map.Local(4, 0));
            SEntMan.GetComponent<CFArrowSupplyComponent>(mob).Arrows = 0;
            Wake(mob, hero);
        });

        await RunNpcs(900);

        string? held = null;
        await Server.WaitPost(() => held = Id(Active(mob)));
        Assert.That(held, Is.EqualTo("CFLongsword"));
    }

}
