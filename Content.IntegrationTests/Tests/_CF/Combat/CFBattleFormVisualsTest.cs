using System.Linq;
using Content.Client.CF.Hud;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>What a client sees of a battle form: the animal in place of the body, and its name for the HUD.</summary>
[TestFixture]
[TestOf(typeof(CFBattleFormSystem))]
public sealed class CFBattleFormVisualsTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);
    private const byte Cat = 5;

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFFormVisualsHero
  components:
  - type: CFCombatant
    level: 3
    armorClass: 12
    maxHp: 30
    currentHp: 30
  - type: CFAbilityScore
  - type: CFCombatOptions
";

    [Test]
    public async Task ACasterLooksLikeTheAnimalAndThenLikeItselfAgain()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var hero = EntityUid.Invalid;
        await Server.WaitPost(() => hero = SEntMan.SpawnEntity("CFFormVisualsHero", map.Local(0, 0)));
        await Pair.RunTicksSync(10);

        var before = 0;
        await Client.WaitPost(() =>
        {
            before = CEntMan.GetComponent<SpriteComponent>(ToClientUid(hero)).AllLayers.Count(layer => layer.Visible);
        });

        await Server.WaitPost(() =>
        {
            var action = SEntMan.SpawnEntity("CFAnimalForm", MapCoordinates.Nullspace);
            var pick = new CFModeImmediateCastEvent(hero, Cat);
            SEntMan.EventBus.RaiseLocalEvent(action, ref pick);
        });
        await Pair.RunTicksSync(10);

        int during = 0;
        string state = null, label = null;
        await Client.WaitPost(() =>
        {
            var uid = ToClientUid(hero);
            var sprite = CEntMan.GetComponent<SpriteComponent>(uid);
            var shown = sprite.AllLayers.Where(layer => layer.Visible).ToList();
            during = shown.Count;
            state = shown.Count == 1 ? shown[0].RsiState.Name : null;
            label = CFHudUiController.BattleFormLabel(CEntMan.GetComponent<CFBattleFormComponent>(uid));
        });

        await Server.WaitPost(() => SEntMan.System<CFBattleFormSystem>().End(hero));
        await Pair.RunTicksSync(10);

        var after = 0;
        await Client.WaitPost(() =>
        {
            after = CEntMan.GetComponent<SpriteComponent>(ToClientUid(hero)).AllLayers.Count(layer => layer.Visible);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.GreaterThan(1), "A human body is drawn in several layers.");
            Assert.That(during, Is.EqualTo(1), "Only the animal is drawn.");
            Assert.That(state, Is.EqualTo("cat"));
            Assert.That(label, Is.EqualTo("Animal Form: Cat"));
            Assert.That(after, Is.EqualTo(before), "The body comes back when the form ends.");
        });
    }

    /// <summary>A form with no sprite would leave the caster looking unchanged, so every one has to name a sprite that exists.</summary>
    [Test]
    public async Task EveryFormHasASpriteThatExists()
    {
        await Client.WaitAssertion(() =>
        {
            var sprites = CEntMan.System<SpriteSystem>();
            Assert.Multiple(() =>
            {
                foreach (var proto in Client.ProtoMan.EnumeratePrototypes<Robust.Shared.Prototypes.EntityPrototype>())
                {
                    if (!proto.TryComp<CFBattleFormSpellComponent>(out var spell, CEntMan.ComponentFactory))
                        continue;

                    foreach (var form in spell.Forms)
                    {
                        Assert.That(form.Sprite, Is.Not.Null, $"{proto.ID} {form.Name} has no sprite.");
                        if (form.Sprite != null)
                            Assert.That(sprites.GetState(form.Sprite), Is.Not.Null, $"{proto.ID} {form.Name}");
                    }
                }
            });
        });
    }
}
