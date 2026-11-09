using System.Collections.Generic;
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
using Content.Shared.CF.Traits;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Commander's banner and tactics, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFTacticSystem))]
public sealed class CFTacticTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 9, 2);
    private const int FarAway = 8;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFTacticHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFTacticHero
  id: CFTacticFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFTacticSystem Tactics => SEntMan.System<CFTacticSystem>();

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private EntityUid Spawn(string id = "CFTacticHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private EntityUid Commander()
    {
        var hero = Spawn();
        var feature = Server.ProtoMan.Index<CFClassPrototype>("Commander").Features.First(f => f.Name == "Commander's Banner");
        Features.Apply(hero, feature.Grants, "banner");
        return hero;
    }

    private Entity<ActionComponent> Grant(EntityUid hero, string id)
    {
        Features.GrantAction(hero, id);
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private int Hurt(EntityUid mob) => 500 - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    [Test]
    public async Task TheSquadIsTheCommanderAndNearestAllies()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var size = 0;
        var nearestIn = false;
        var furthestIn = true;
        var enemyIn = true;
        var selfFirst = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var near = Spawn(x: 1);
            Spawn(x: 2);
            var third = Spawn(x: 3);
            var enemy = Spawn("CFTacticFoe", 1);
            var squad = Tactics.Squad(hero);
            size = squad.Count;
            selfFirst = squad[0] == hero;
            nearestIn = squad.Contains(near);
            furthestIn = squad.Contains(third);
            enemyIn = squad.Contains(enemy);
        });

        Assert.Multiple(() =>
        {
            Assert.That(size, Is.EqualTo(3), "The Commander and 2 + Intelligence 0.");
            Assert.That(selfFirst, Is.True);
            Assert.That(nearestIn, Is.True);
            Assert.That(furthestIn, Is.False);
            Assert.That(enemyIn, Is.False);
        });
    }

    [Test]
    public async Task TheBannerGivesItsAuraWhileCarried()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid hero = default, ally = default, banner = default;
        var fear = new HashSet<ProtoId<CFTraitPrototype>> { "Fear" };
        int without = 1, with = 0, own = 0, far = 1, dropped = 1;
        await Server.WaitPost(() =>
        {
            hero = Commander();
            ally = Spawn(x: 2);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            without = SEntMan.System<CFBonusSystem>().Total(ally, CFBonusStat.Will, traits: fear);
            banner = Spawn("CFCommanderBanner");
            Hands.TryPickupAnyHand(hero, banner);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            var bonuses = SEntMan.System<CFBonusSystem>();
            with = bonuses.Total(ally, CFBonusStat.Will, traits: fear);
            own = bonuses.Total(hero, CFBonusStat.Will, traits: fear);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(ally, _map.Local(FarAway, 0));
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() =>
        {
            far = SEntMan.System<CFBonusSystem>().Total(ally, CFBonusStat.Will, traits: fear);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(ally, _map.Local(2, 0));
            Hands.TryDrop(hero, banner);
        });
        await Pair.RunSeconds(0.5f);
        await Server.WaitPost(() => dropped = SEntMan.System<CFBonusSystem>().Total(ally, CFBonusStat.Will, traits: fear));

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.EqualTo(0));
            Assert.That(with, Is.EqualTo(1));
            Assert.That(own, Is.EqualTo(1));
            Assert.That(far, Is.EqualTo(0));
            Assert.That(dropped, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ATacticNeedsTheBanner()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool empty = true, held = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var action = Grant(hero, "CFActTacticPincerAttack");
            var first = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref first);
            empty = !first.Cancelled;
            Hands.TryPickupAnyHand(hero, Spawn("CFCommanderBanner"));
            var second = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref second);
            held = !second.Cancelled;
        });

        Assert.Multiple(() =>
        {
            Assert.That(empty, Is.False);
            Assert.That(held, Is.True);
        });
    }

    [Test]
    public async Task PincerAttackLeavesAdjacentEnemiesOffGuardToTheSquad()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool toAlly = false, toStranger = true, distant = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Hands.TryPickupAnyHand(hero, Spawn("CFCommanderBanner"));
            var ally = Spawn(x: 2);
            var stranger = Spawn(x: FarAway);
            var enemy = Spawn("CFTacticFoe", 3);
            var other = Spawn("CFTacticFoe", 6);
            Actions.PerformAction(hero, Grant(hero, "CFActTacticPincerAttack"), new CFActivityInstantEvent());
            var conditions = SEntMan.System<CFConditionSystem>();
            toAlly = conditions.IsOffGuardTo(enemy, ally);
            toStranger = conditions.IsOffGuardTo(enemy, stranger);
            distant = conditions.IsOffGuardTo(other, ally);
        });

        Assert.Multiple(() =>
        {
            Assert.That(toAlly, Is.True);
            Assert.That(toStranger, Is.False);
            Assert.That(distant, Is.False);
        });
    }

    [Test]
    public async Task StrikeHardMakesTheSquadMemberAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hurt = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Hands.TryPickupAnyHand(hero, Spawn("CFCommanderBanner"));
            var ally = Spawn(x: 2);
            var enemy = Spawn("CFTacticFoe", 3);
            var action = Grant(hero, "CFActTacticStrikeHard");
            for (var i = 0; i < 10 && hurt == 0; i++)
            {
                Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = ally });
                hurt = Hurt(enemy);
            }
        });

        Assert.That(hurt, Is.GreaterThan(0));
    }

    [Test]
    public async Task TacticalTakedownNeedsTwoOfTheSquad()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var alone = 0f;
        var together = 0f;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Hands.TryPickupAnyHand(hero, Spawn("CFCommanderBanner"));
            var enemy = Spawn("CFTacticFoe", 1);
            var action = Grant(hero, "CFActTacticTakedown");
            var pool = SEntMan.GetComponent<CFActionPoolComponent>(hero);

            var before = pool.Stamina;
            Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = enemy });
            alone = before - pool.Stamina;

            Spawn(x: 2);
            before = pool.Stamina;
            Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = enemy });
            together = before - pool.Stamina;
        });

        Assert.Multiple(() =>
        {
            Assert.That(alone, Is.EqualTo(0f), "It could not be used, so it costs nothing.");
            Assert.That(together, Is.EqualTo(2f));
        });
    }

    [Test]
    public async Task EveryTacticChoiceCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitPost(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Commander"))
            {
                Features.Apply(Spawn(), choice.Grants, choice.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(21));
    }
}
