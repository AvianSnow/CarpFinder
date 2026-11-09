using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Spells;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Spellshapes, through the same perform steps the action bar uses.</summary>
[TestFixture]
[TestOf(typeof(CFSpellshapeSystem))]
public sealed class CFSpellshapeTest : GameTest
{
    private const string Reach = "CFActReachSpell";
    private const string Widen = "CFActWidenSpell";
    private const string Cast = "CFSpellshapeTestCast";

    private static readonly Box2i Footprint = new(-1, -1, 6, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: cfSpell
  id: CFSpellshapeTestSpell
  name: Test

- type: cfSpell
  id: CFSpellshapeTestLasting
  name: Test
  duration: 1 minute

- type: entity
  parent: CFSpellInstant
  id: CFSpellshapeTestCast
  components:
  - type: CFSpell
    spell: CFSpellshapeTestSpell
  - type: CFTargeting
    delivery: Self
    rangeFeet: 30
  - type: CFRadialSlot
    group: Spell
    actions: 2

- type: entity
  parent: CFSpellInstant
  id: CFSpellshapeTestLastingCast
  components:
  - type: CFSpell
    spell: CFSpellshapeTestLasting

- type: entity
  parent: CFSpellInstant
  id: CFSpellshapeTestBurst
  components:
  - type: CFSpell
    spell: CFSpellshapeTestSpell
  - type: CFAreaEffect
    shape: Burst
    sizeFeet: 10

- type: entity
  parent: BaseAction
  id: CFSpellshapeTestBomb
  components:
  - type: CFTargeting
    rangeFeet: 30

- type: entity
  parent: MobHuman
  id: CFSpellshapeTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 100
    currentHp: 100
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha
  - type: CFActionPool
    maxActions: 30
";

    private CFDevMap _map = default!;

    private CFSpellshapeSystem Shapes => SEntMan.System<CFSpellshapeSystem>();

    private EntityUid Hero()
    {
        return SEntMan.SpawnEntity("CFSpellshapeTestHero", _map.Local(0, 0));
    }

    private Entity<ActionComponent> Grant(EntityUid hero, string id)
    {
        SEntMan.System<CFCharacterFeatureSystem>().GrantAction(hero, id);
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private void Use(EntityUid hero, Entity<ActionComponent> action)
    {
        SEntMan.System<SharedActionsSystem>().PerformAction(hero, action);
    }

    private bool Waiting(EntityUid hero)
    {
        return SEntMan.HasComponent<CFActiveSpellshapeComponent>(hero);
    }

    private EntityUid Action(string id)
    {
        return SEntMan.SpawnEntity(id, MapCoordinates.Nullspace);
    }

    private MapCoordinates Feet(EntityUid hero, float feet)
    {
        var at = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);
        return new MapCoordinates(at.Position + new Vector2(feet / 5f, 0f), at.MapId);
    }

    [Test]
    public async Task ReachSpellAddsThirtyFeetToASpellOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool before = true, after = false, tooFar = true, bomb = true, waiting = false;
        await Server.WaitPost(() =>
        {
            var effects = SEntMan.System<CFEffectSystem>();
            var hero = Hero();
            var spell = Action(Cast);
            before = effects.InRange(spell, hero, Feet(hero, 40f));
            Use(hero, Grant(hero, Reach));
            waiting = Waiting(hero);
            after = effects.InRange(spell, hero, Feet(hero, 40f));
            tooFar = effects.InRange(spell, hero, Feet(hero, 70f));
            bomb = effects.InRange(Action("CFSpellshapeTestBomb"), hero, Feet(hero, 40f));
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(waiting, Is.True, "Paying for the spellshape must not end it.");
            Assert.That(after, Is.True);
            Assert.That(tooFar, Is.False);
            Assert.That(bomb, Is.False, "Only spells are changed.");
        });
    }

    [TestCase(CFAreaShape.Burst, 5, 0)]
    [TestCase(CFAreaShape.Burst, 10, 5)]
    [TestCase(CFAreaShape.Burst, 20, 5)]
    [TestCase(CFAreaShape.Cone, 15, 5)]
    [TestCase(CFAreaShape.Cone, 30, 10)]
    [TestCase(CFAreaShape.Line, 10, 5)]
    [TestCase(CFAreaShape.Line, 60, 10)]
    [TestCase(CFAreaShape.Emanation, 20, 0)]
    [TestCase(CFAreaShape.Square, 20, 0)]
    public async Task WidenSpellFollowsTheTable(CFAreaShape shape, int size, int expected)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var extra = -1;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            Use(hero, Grant(hero, Widen));
            extra = Shapes.ExtraAreaFeet(Action(Cast), hero, new CFAreaEffectComponent { Shape = shape, SizeFeet = size });
        });

        Assert.That(extra, Is.EqualTo(expected));
    }

    [Test]
    public async Task WidenSpellIgnoresASpellWithADuration()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var extra = -1;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            Use(hero, Grant(hero, Widen));
            extra = Shapes.ExtraAreaFeet(
                Action("CFSpellshapeTestLastingCast"),
                hero,
                new CFAreaEffectComponent { Shape = CFAreaShape.Burst, SizeFeet = 20 });
        });

        Assert.That(extra, Is.Zero);
    }

    /// <summary>A real burst, with a creature just past its edge. The aim stays on the caster, who is not caught in it.</summary>
    [Test]
    public async Task AWidenedBurstCatchesMore()
    {
        _map = await CFDevMap.Load(Pair, Footprint, avoidStructures: true);
        int plain = -1, widened = -1;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            var foe = SEntMan.SpawnEntity("CFSpellshapeTestHero", _map.Local(3, 0));
            SEntMan.GetComponent<CFFactionComponent>(foe).Faction = "Monster";
            var effects = SEntMan.System<CFEffectSystem>();
            var spell = Action("CFSpellshapeTestBurst");
            var aim = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);

            plain = effects.ResolveArea(spell, hero, aim);
            Use(hero, Grant(hero, Widen));
            widened = effects.ResolveArea(spell, hero, aim);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.Zero);
            Assert.That(widened, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CastingUsesItUpAndPaysNormally()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var waiting = true;
        float before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            Use(hero, Grant(hero, Reach));
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);
            before = pool.Stamina;
            Use(hero, Grant(hero, Cast));
            waiting = Waiting(hero);
            after = pool.Stamina;
        });

        Assert.Multiple(() =>
        {
            Assert.That(waiting, Is.False);
            Assert.That(after, Is.EqualTo(before - 2f));
        });
    }

    [Test]
    public async Task AnyOtherActionLosesIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = Hero();
            Use(hero, Grant(hero, Reach));
        });

        await Pair.RunTicksSync(2);

        var stillThere = false;
        await Server.WaitPost(() =>
        {
            stillThere = Waiting(hero);
            SEntMan.System<CFActionPoolSystem>().TrySpendActions(hero, 1);
        });

        await Pair.RunTicksSync(2);

        var after = true;
        await Server.WaitPost(() => after = Waiting(hero));

        Assert.Multiple(() =>
        {
            Assert.That(stillThere, Is.True);
            Assert.That(after, Is.False);
        });
    }

    [Test]
    public async Task ItFadesAfterARound()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default;
        await Server.WaitPost(() =>
        {
            hero = Hero();
            Use(hero, Grant(hero, Reach));
        });

        await Pair.RunSeconds(6.5f);

        var after = true;
        await Server.WaitPost(() => after = Waiting(hero));
        Assert.That(after, Is.False);
    }
}
