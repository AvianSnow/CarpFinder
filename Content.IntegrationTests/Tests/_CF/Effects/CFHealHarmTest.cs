using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Effects;

/// <summary>
/// Heal and Harm picked as 1, 2 or 3 actions from the wheel, the living and the undead, the Cleric feats that change
/// them, and Resist Energy's energy picked the same way.
/// </summary>
[TestFixture]
[TestOf(typeof(CFVitalEnergyEffect))]
public sealed class CFHealHarmTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 9, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFHealHarmTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFHealHarmTestHero
  id: CFHealHarmTestUndead
  components:
  - type: CFVoidHealing
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFEffectSystem Effects => SEntMan.System<CFEffectSystem>();

    private EntityUid Spawn(int x, string id = "CFHealHarmTestHero", int hp = 50)
    {
        var mob = SEntMan.SpawnEntity(id, _map.Local(x, 0));
        SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp = hp;
        return mob;
    }

    private int Hp(EntityUid mob)
    {
        return SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;
    }

    private EntityUid Spell(string id, byte mode)
    {
        var spell = SEntMan.SpawnEntity(id, MapCoordinates.Nullspace);
        SEntMan.GetComponent<CFModeChoiceComponent>(spell).Chosen = mode;
        return spell;
    }

    [Test]
    public async Task EachActionCountHasItsOwnCostRangeAndHealing()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int touchCost = 0, rangedCost = 0, areaCost = 0, ranged = 0, touch = 0;
        bool touchReaches = true, rangedReaches = false;
        await Server.WaitPost(() =>
        {
            var pool = SEntMan.System<CFActionPoolSystem>();
            var cleric = Spawn(0, hp: 100);
            var ally = Spawn(3);

            var heal = Spell("CFHeal", 1);
            touchCost = pool.ActionCount(heal);
            touchReaches = Effects.InRange(heal, cleric, ally);

            var two = Spell("CFHeal", 2);
            rangedCost = pool.ActionCount(two);
            rangedReaches = Effects.InRange(two, cleric, ally);
            Effects.ResolveTarget(two, cleric, ally);
            ranged = Hp(ally) - 50;

            var hurt = Spawn(1);
            Effects.ResolveTarget(heal, cleric, hurt);
            touch = Hp(hurt) - 50;

            areaCost = pool.ActionCount(Spell("CFHeal", 3));
        });

        Assert.Multiple(() =>
        {
            Assert.That(touchCost, Is.EqualTo(1));
            Assert.That(rangedCost, Is.EqualTo(2));
            Assert.That(areaCost, Is.EqualTo(3));
            Assert.That(touchReaches, Is.False, "The 1-action version is a touch.");
            Assert.That(rangedReaches, Is.True);
            Assert.That(ranged, Is.InRange(9, 16), "Two actions heal 1d8+8.");
            Assert.That(touch, Is.InRange(1, 8), "One action heals 1d8.");
        });
    }

    [Test]
    public async Task HealHurtsTheUndeadAndHarmHealsThem()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int healOnUndead = 0, harmOnUndead = 0, harmOnLiving = 0;
        await Server.WaitPost(() =>
        {
            var cleric = Spawn(0, hp: 100);
            var zombie = Spawn(1, "CFHealHarmTestUndead");
            Effects.ResolveTarget(Spell("CFHeal", 2), cleric, zombie);
            healOnUndead = Hp(zombie) - 50;

            var other = Spawn(1, "CFHealHarmTestUndead");
            Effects.ResolveTarget(Spell("CFHarm", 2), cleric, other);
            harmOnUndead = Hp(other) - 50;

            var living = Spawn(1, "CFHealHarmTestUndead");
            SEntMan.RemoveComponent<CFVoidHealingComponent>(living);
            Effects.ResolveTarget(Spell("CFHarm", 2), cleric, living);
            harmOnLiving = Hp(living) - 50;
        });

        Assert.Multiple(() =>
        {
            Assert.That(healOnUndead, Is.InRange(-16, 0), "Vitality damages the undead: 1d8, with no +8, doubled at most.");
            Assert.That(harmOnUndead, Is.InRange(9, 16), "Harm heals the undead, 1d8+8 with 2 actions.");
            Assert.That(harmOnLiving, Is.InRange(-16, 0));
        });
    }

    [Test]
    public async Task TheEmanationTakesEveryoneButTheCaster()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        int caster = 0, near = 0, far = 0, zombie = 0;
        await Server.WaitPost(() =>
        {
            var cleric = Spawn(0);
            var ally = Spawn(2);
            var distant = Spawn(8);
            var undead = Spawn(3, "CFHealHarmTestUndead");
            var heal = Spell("CFHeal", 3);
            var mode = SEntMan.GetComponent<CFModeChoiceComponent>(heal).ChosenOption()!;
            Effects.ResolveModeArea(heal, cleric, mode, SEntMan.System<SharedTransformSystem>().GetMapCoordinates(cleric));
            caster = Hp(cleric) - 50;
            near = Hp(ally) - 50;
            far = Hp(distant) - 50;
            zombie = Hp(undead) - 50;
        });

        Assert.Multiple(() =>
        {
            Assert.That(caster, Is.EqualTo(0), "An emanation leaves out its source.");
            Assert.That(near, Is.InRange(1, 8));
            Assert.That(far, Is.EqualTo(0), "40 feet away is outside the 30-foot emanation.");
            Assert.That(zombie, Is.InRange(-16, 0));
        });
    }

    [Test]
    public async Task ClericFeatsChangeHealAndHarm()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int communal = 0, sapped = 0;
        bool coneWithout = true, coneWith = false;
        await Server.WaitPost(() =>
        {
            var cleric = Spawn(0);
            var options = SEntMan.GetComponent<CFCombatOptionsComponent>(cleric);
            var heal = Spell("CFHeal", 2);
            var cone = SEntMan.GetComponent<CFModeChoiceComponent>(heal).Modes.Find(m => m.Requires == CFModeRequirement.DirectedChannel)!;
            coneWithout = CFModeRequirements.Met(SEntMan, cleric, cone);
            options.DirectedChannel = true;
            coneWith = CFModeRequirements.Met(SEntMan, cleric, cone);

            options.CommunalHealing = true;
            Effects.ResolveTarget(heal, cleric, Spawn(1));
            communal = Hp(cleric) - 50;

            var reaper = Spawn(0);
            SEntMan.GetComponent<CFCombatOptionsComponent>(reaper).SapLife = true;
            var foe = Spawn(1, "CFHealHarmTestUndead");
            SEntMan.RemoveComponent<CFVoidHealingComponent>(foe);
            SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp = 100;
            Effects.ResolveTarget(Spell("CFHarm", 2), reaper, foe);
            sapped = Hp(reaper) - 50;
        });

        Assert.Multiple(() =>
        {
            Assert.That(coneWithout, Is.False, "The cone needs Directed Channel.");
            Assert.That(coneWith, Is.True);
            Assert.That(communal, Is.EqualTo(1), "Communal Healing gives the caster the spell's rank.");
            Assert.That(sapped, Is.InRange(0, 1), "Sap Life gives the rank when the foe is hurt; a critical success on its save hurts it not at all.");
        });
    }

    [Test]
    public async Task ResistEnergyResistsThePickedEnergy()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int cold = 0, fire = 0;
        await Server.WaitPost(() =>
        {
            var wizard = Spawn(0);
            var ally = Spawn(1);
            var spell = SEntMan.SpawnEntity("CFResistEnergy", MapCoordinates.Nullspace);
            var mode = SEntMan.GetComponent<CFModeChoiceComponent>(spell);
            mode.Chosen = mode.Modes.Find(m => m.DamageType == "Cold")!.Value;
            Effects.ResolveTarget(spell, wizard, ally);

            var bonuses = SEntMan.System<CFBonusSystem>();
            cold = 10 - bonuses.ModifyDamage(ally, "Cold", 10);
            fire = 10 - bonuses.ModifyDamage(ally, "Fire", 10);
        });

        Assert.Multiple(() =>
        {
            Assert.That(cold, Is.EqualTo(5));
            Assert.That(fire, Is.EqualTo(0));
        });
    }
}
