using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Thaumaturge: Ring Bell's save, the Chalice's sip, the Tome's skills and Esoteric Warden.</summary>
[TestFixture]
[TestOf(typeof(CFSaveOrEffect))]
public sealed class CFThaumaturgeTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    private static readonly ProtoId<CFClassChoicePrototype> Bell = "ImplementBell";
    private static readonly ProtoId<CFClassChoicePrototype> Tome = "ImplementTome";
    private static readonly EntProtoId Sip = "CFActSipChalice";

    public override PoolSettings PoolSettings => new() { Connected = false };

    // A thrall fails every check, so the bell's save always fails against it.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFThaumaturgeTestHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFThaumaturgeTestHero
  id: CFThaumaturgeTestFoe
  components:
  - type: CFFaction
    faction: Monster
  - type: CFThrall
";

    private CFDevMap _map = default!;

    private EntityUid Spawn(string id = "CFThaumaturgeTestHero", int x = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, 0));
    }

    [Test]
    public async Task ImplementsHaveWhatTheySay()
    {
        await Server.WaitAssertion(() =>
        {
            var bell = Server.ProtoMan.Index(Bell).Grants.OfType<CFGrantReaction>().Single().Reaction;
            Assert.That(bell.OnEnemy.Single(), Is.TypeOf<CFSaveOrEffect>());
            Assert.That(bell.RangeFeet, Is.EqualTo(30));

            var tome = Server.ProtoMan.Index(Tome).Choices.Single();
            Assert.That(tome.Kind, Is.EqualTo(CFOptionChoiceKind.Skill));
            Assert.That(tome.Count, Is.EqualTo(2));
            Assert.That(tome.Daily, Is.True);
        });
    }

    [Test]
    public async Task TheBellEnfeeblesOnAFailedSaveAndTheSipGivesAtLeastThree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int enfeebled = 0, temp = 0, ac = 0, saves = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var foe = Spawn("CFThaumaturgeTestFoe", 1);
            var effects = SEntMan.System<CFEffectSystem>();
            var bell = Server.ProtoMan.Index(Bell).Grants.OfType<CFGrantReaction>().Single().Reaction;
            effects.Run(bell.OnEnemy, new CFEffectArgs { User = hero, Source = hero, Target = foe });
            enfeebled = SEntMan.System<CFConditionSystem>().Value(foe, CFConditionKind.Enfeebled);

            if (Server.ProtoMan.Index(Sip).TryComp<CFDegreeEffectsComponent>(out var sip, SEntMan.ComponentFactory))
                effects.Run(sip.Effects, new CFEffectArgs { User = hero, Source = hero, Target = hero });

            temp = SEntMan.GetComponent<CFCombatantComponent>(hero).TemporaryHp;

            // Esoteric Warden: Exploit Vulnerability wards the Thaumaturge.
            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).EsotericWarden = true;
            SEntMan.System<CFActivitySystem>().Mark(hero, foe, prey: false);
            var bonuses = SEntMan.System<CFBonusSystem>();
            ac = bonuses.TotalOfType(hero, CFBonusStat.ArmorClass, CFBonusType.Status);
            saves = bonuses.TotalOfType(hero, CFBonusStat.Saves, CFBonusType.Status);
        });

        Assert.Multiple(() =>
        {
            Assert.That(enfeebled, Is.EqualTo(1));
            Assert.That(temp, Is.EqualTo(3), "Level 1: the least a sip gives");
            Assert.That(ac, Is.EqualTo(1));
            Assert.That(saves, Is.EqualTo(1));
        });
    }
}
