using Robust.Shared.Prototypes;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Feats whose action only applies a short bonus: Twin Parry and Raise Symbol. And Story Crooner's bonus.</summary>
[TestFixture]
[TestOf(typeof(CFEffectActionSystem))]
public sealed class CFEffectActionFeatTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFEffectActionTestHero
  components:
  - type: CFCombatant
    level: 4
    maxHp: 50
    currentHp: 50
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
";

    private CFDevMap _map = default!;

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private EntityUid Hero() => SEntMan.SpawnEntity("CFEffectActionTestHero", _map.Local(0, 0));

    private Entity<ActionComponent> Grant(EntityUid hero, string id)
    {
        SEntMan.System<CFCharacterFeatureSystem>().GrantAction(hero, id);
        return SEntMan.System<SharedActionsSystem>().GetActions(hero)
            .First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool Use(EntityUid hero, Entity<ActionComponent> action)
    {
        var attempt = new ActionAttemptEvent(hero, null);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        if (attempt.Cancelled)
            return false;

        SEntMan.System<SharedActionsSystem>().PerformAction(hero, action);
        return true;
    }

    [Test]
    public async Task TwinParryNeedsTwoWeaponsAndRaiseSymbolHelpsSaves()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool emptyHanded = true, armed = false;
        int ac = 0, saves = 0;
        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var hero = Hero();
            var parry = Grant(hero, "CFActTwinParry");
            emptyHanded = Use(hero, parry);

            hands.TryPickupAnyHand(hero, SEntMan.SpawnEntity("CFDagger", _map.Local(0, 0)), checkActionBlocker: false);
            hands.TryPickupAnyHand(hero, SEntMan.SpawnEntity("CFDagger", _map.Local(0, 0)), checkActionBlocker: false);
            armed = Use(hero, parry);
            ac = Bonuses.Total(hero, CFBonusStat.ArmorClass);

            var cleric = Hero();
            Use(cleric, Grant(cleric, "CFActRaiseSymbol"));
            saves = Bonuses.Total(cleric, CFBonusStat.Saves, CFBonusStat.Will);
        });

        Assert.Multiple(() =>
        {
            Assert.That(emptyHanded, Is.False);
            Assert.That(armed, Is.True);
            Assert.That(ac, Is.EqualTo(1));
            Assert.That(saves, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task StoryCroonerHelpsPerformanceOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int performance = 0, stealth = -1;
        await Server.WaitPost(() =>
        {
            var hero = Hero();
            SEntMan.System<CFCharacterSpawnSystem>().ApplyFeat(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFStoryCrooner")));
            performance = Bonuses.Total(hero, CFBonusStat.SkillChecks, skill: "Performance");
            stealth = Bonuses.Total(hero, CFBonusStat.SkillChecks, skill: "Stealth");
        });

        Assert.Multiple(() =>
        {
            Assert.That(performance, Is.EqualTo(1));
            Assert.That(stealth, Is.Zero);
        });
    }
}
