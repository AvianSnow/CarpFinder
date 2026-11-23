using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Commander's new tactics and feats, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFTacticSystem))]
public sealed class CFCommanderTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 9, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFCommanderHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    classId: Commander
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 40
      Dexterity: 0
      Constitution: 0
      Intelligence: 0
      Wisdom: 0
      Charisma: 0
  - type: CFCombatOptions
  - type: CFExperience
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFCommanderHero
  id: CFCommanderFoe
  components:
  - type: CFAbilityScore
    abilityModifiers:
      Strength: 0
      Dexterity: 0
      Constitution: 0
      Intelligence: 0
      Wisdom: 0
      Charisma: 0
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFTacticSystem Tactics => SEntMan.System<CFTacticSystem>();

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private SharedHandsSystem Hands => SEntMan.System<SharedHandsSystem>();

    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();

    private EntityUid Spawn(string id = "CFCommanderHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    /// <summary>A Commander with the banner in hand and the tactic or feat asked for.</summary>
    private EntityUid Commander(params string[] options)
    {
        var hero = Spawn();
        var feature = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Commander")).Features.First(f => f.Name == "Commander's Banner");
        Features.Apply(hero, feature.Grants, "banner");
        foreach (var option in options)
        {
            if (Server.ProtoMan.TryIndex<CFClassChoicePrototype>(option, out var choice))
                Features.Apply(hero, choice.Grants, option);
            else
                Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(option)).Grants, option);
        }

        var banner = SEntMan.SpawnEntity("CFCommanderBanner", _map.Local(0, 0));
        Hands.TryPickupAnyHand(hero, banner, animate: false);
        return hero;
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private bool Allowed(EntityUid hero, Entity<ActionComponent> action, EntityUid? target = null)
    {
        var attempt = new ActionAttemptEvent(hero, target);
        SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
        return !attempt.Cancelled;
    }

    private Vector2 Where(EntityUid uid) => SEntMan.System<SharedTransformSystem>().GetWorldPosition(uid);

    /// <summary>Passage of Lines trades the places of squad members standing together.</summary>
    [Test]
    public async Task PassageOfLinesSwapsNeighbours()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Vector2 heroBefore = default, allyBefore = default, heroAfter = default, allyAfter = default;
        await Server.WaitPost(() =>
        {
            var hero = Commander("TacticFirstPassageOfLines");
            var ally = Spawn(x: 1);
            heroBefore = Where(hero);
            allyBefore = Where(ally);
            Actions.PerformAction(hero, Action(hero, "CFActTacticPassageOfLines"), new CFActivityInstantEvent());
            heroAfter = Where(hero);
            allyAfter = Where(ally);
        });

        Assert.Multiple(() =>
        {
            Assert.That(heroAfter, Is.EqualTo(allyBefore));
            Assert.That(allyAfter, Is.EqualTo(heroBefore));
        });
    }

    /// <summary>Protective Screen: a screened squad member sets off no reactions for a while.</summary>
    [Test]
    public async Task ProtectiveScreenCoversASquadMember()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool before = true, after = false, enemyAllowed = true;
        await Server.WaitPost(() =>
        {
            var hero = Commander("TacticFirstProtectiveScreen");
            var ally = Spawn(x: 1);
            var foe = Spawn("CFCommanderFoe", 2);
            var action = Action(hero, "CFActTacticProtectiveScreen");
            before = Conditions.IsScreened(ally);
            Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = ally });
            after = Conditions.IsScreened(ally);
            Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = foe });
            enemyAllowed = Conditions.IsScreened(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False);
            Assert.That(after, Is.True);
            Assert.That(enemyAllowed, Is.False, "Only a squad member.");
        });
    }

    /// <summary>Mountaineering Training gives the squad a climb Speed for a moment.</summary>
    [Test]
    public async Task MountaineeringTrainingGivesAClimbSpeed()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var until = System.TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            var hero = Commander("TacticFirstMountaineeringTraining");
            var ally = Spawn(x: 1);
            Actions.PerformAction(hero, Action(hero, "CFActTacticMountaineeringTraining"), new CFActivityInstantEvent());
            until = SEntMan.GetComponent<CFCombatOptionsComponent>(ally).ClimbSpeedUntil;
        });

        Assert.That(until, Is.GreaterThan(System.TimeSpan.Zero));
    }

    /// <summary>Double Team: Strength 40 shoves, and the member on the other side Strikes.</summary>
    [Test]
    public async Task DoubleTeamShovesAndStrikes()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var moving = false;
        var hurt = 0;
        var allowedAlone = true;
        await Server.WaitPost(() =>
        {
            var hero = Commander("TacticFirstDoubleTeam");
            var action = Action(hero, "CFActTacticDoubleTeam");
            var far = Spawn("CFCommanderFoe", 7);
            allowedAlone = Allowed(hero, action, far);

            var foe = Spawn("CFCommanderFoe", 1);
            Spawn(x: 2);
            for (var i = 0; i < 10 && hurt == 0; i++)
            {
                SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp = 500;
                Actions.PerformAction(hero, action, new CFActivityTargetEvent { Target = foe });
                moving |= SEntMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(foe).LinearVelocity.LengthSquared() > 0.1f;
                hurt = 500 - SEntMan.GetComponent<CFCombatantComponent>(foe).CurrentHp;
                SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(foe, Vector2.Zero);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(moving, Is.True, "Shoved.");
            Assert.That(hurt, Is.GreaterThan(0), "Struck by the member it was shoved toward.");
        });
    }

    /// <summary>End It! needs a recent drop and the numbers, then frightens the enemies about.</summary>
    [Test]
    public async Task EndItNeedsADropAndFrightens()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool cold = true, ready = false;
        var frightened = 0;
        await Server.WaitPost(() =>
        {
            var hero = Commander("TacticFirstEndIt");
            Spawn(x: 1);
            var foe = Spawn("CFCommanderFoe", 2);
            var action = Action(hero, "CFActTacticEndIt");
            cold = Allowed(hero, action);

            SEntMan.GetComponent<CFCombatOptionsComponent>(hero).LastDroppedEnemyAt = Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime;
            ready = Allowed(hero, action);
            // A class DC built on nothing is low: cast until the save fails.
            for (var i = 0; i < 20 && frightened == 0; i++)
            {
                Actions.PerformAction(hero, action, new CFActivityInstantEvent());
                frightened = SEntMan.TryGetComponent<CFConditionComponent>(foe, out var cond) ? cond.Frightened : 0;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(cold, Is.False, "No enemy dropped lately.");
            Assert.That(ready, Is.True);
            Assert.That(frightened, Is.GreaterThan(0));
        });
    }

    /// <summary>A planted banner still counts, and renews the squad's temporary Hit Points.</summary>
    [Test]
    public async Task APlantedBannerStandsForTheCommander()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool planted = false, counts = false, holdsNothing = false, back = false;
        var tempHp = 0;
        EntityUid hero = default, ally = default;
        await Server.WaitPost(() =>
        {
            hero = Commander("CFPlantBanner");
            ally = Spawn(x: 1);
            Actions.PerformAction(hero, Action(hero, "CFActPlantBanner"), new CFActivityInstantEvent());
            planted = Tactics.PlantedBanner(hero) != null;
            holdsNothing = !Hands.EnumerateHeld(hero).Any();
            counts = Tactics.HasBanner(hero);
        });

        await Server.WaitRunTicks(5);
        await Server.WaitPost(() =>
        {
            tempHp = SEntMan.GetComponent<CFCombatantComponent>(ally).TemporaryHp;
            Actions.PerformAction(hero, Action(hero, "CFActPlantBanner"), new CFActivityInstantEvent());
            back = Tactics.PlantedBanner(hero) == null && Hands.EnumerateHeld(hero).Any();
        });

        Assert.Multiple(() =>
        {
            Assert.That(planted, Is.True);
            Assert.That(holdsNothing, Is.True);
            Assert.That(counts, Is.True, "Planted, it is still the Commander's banner.");
            Assert.That(tempHp, Is.EqualTo(4), "4 temporary Hit Points at level 1.");
            Assert.That(back, Is.True, "Taken back.");
        });
    }

    /// <summary>Guiding Shot's guidance goes to the next other attacker, once.</summary>
    [Test]
    public async Task GuidanceGoesToTheNextOtherAttacker()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int self = -1, ally = 0, again = -1;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var friend = Spawn(x: 1);
            var foe = Spawn("CFCommanderFoe", 2);
            Conditions.SetGuidance(foe, hero, 2, 6f);
            self = Conditions.TakeGuidance(foe, hero);
            ally = Conditions.TakeGuidance(foe, friend);
            again = Conditions.TakeGuidance(foe, friend);
        });

        Assert.Multiple(() =>
        {
            Assert.That(self, Is.EqualTo(0), "Not the one who shot.");
            Assert.That(ally, Is.EqualTo(2));
            Assert.That(again, Is.EqualTo(0), "Used up.");
        });
    }

    /// <summary>Armor Regiment Training: heavy armor is a Bulk lighter.</summary>
    [Test]
    public async Task ArmorRegimentTrainingLightensHeavyArmor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = 0, after = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var armor = Spawn("CFHalfPlate");
            Assert.That(SEntMan.System<Content.Shared.Inventory.InventorySystem>().TryEquip(hero, armor, "outerClothing", force: true), Is.True);
            var bulk = SEntMan.System<CFEncumbranceSystem>();
            before = bulk.CarriedTenths(hero);
            Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFArmoredRegimentTraining")).Grants, "regiment");
            after = bulk.CarriedTenths(hero);
        });

        Assert.That(before - after, Is.EqualTo(10), "One Bulk less.");
    }

    /// <summary>Tactical Expansion's two picks are two more tactics.</summary>
    [Test]
    public async Task TacticalExpansionGivesThePickedTactics()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var ids = new List<string>();
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var xp = SEntMan.EnsureComponent<CFExperienceComponent>(hero);
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFTacticalExpansion", Slot = 0, Value = "pick:EndIt" });
            xp.OptionPicks.Add(new CFOptionPick { Source = "CFTacticalExpansion", Slot = 1, Value = "pick:PassageOfLines" });
            Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFTacticalExpansion")).Grants, "CFTacticalExpansion");
            ids.AddRange(Actions.GetActions(hero).Select(a => SEntMan.GetComponent<MetaDataComponent>(a).EntityPrototype?.ID ?? ""));
        });

        Assert.That(ids, Is.SupersetOf(new[] { "CFActTacticEndIt", "CFActTacticPassageOfLines" }));
    }

    /// <summary>Every tactic option applies, in every slot.</summary>
    [Test]
    public async Task EveryTacticChoiceCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitAssertion(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Commander"))
            {
                Features.Apply(Spawn(), choice.Grants, choice.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(42), "14 tactics in each of three slots.");
    }
}
