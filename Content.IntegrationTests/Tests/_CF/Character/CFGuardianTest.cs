using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>The Guardian's feats, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFTauntSystem))]
public sealed class CFGuardianTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 7, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFGuardianHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
    classId: Guardian
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
  parent: CFGuardianHero
  id: CFGuardianFoe
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

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private CFBonusSystem Bonuses => SEntMan.System<CFBonusSystem>();

    private CFTauntSystem Taunts => SEntMan.System<CFTauntSystem>();

    private EntityUid Spawn(string id = "CFGuardianHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private void GiveFeat(EntityUid hero, string id)
    {
        Features.Apply(hero, Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>(id)).Grants, id);
    }

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    /// <summary>Bodyguard: the Taunt's penalty is -2 against the sworn ally.</summary>
    [Test]
    public async Task BodyguardsChargeIsHarderToGoFor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int other = 0, charge = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFBodyguard");
            var friend = Spawn(x: 1);
            var bystander = Spawn(x: 2);
            var foe = Spawn("CFGuardianFoe", 3);
            Taunts.SwearToProtect(hero, friend);

            Taunts.Taunt(hero, foe);
            other = Taunts.NoteAttack(foe, bystander);
            Taunts.Taunt(hero, foe);
            charge = Taunts.NoteAttack(foe, friend);
        });

        Assert.Multiple(() =>
        {
            Assert.That(other, Is.EqualTo(-1));
            Assert.That(charge, Is.EqualTo(-2));
        });
    }

    /// <summary>Covering Stance covers the allies beside the Guardian while it lasts. Auras are applied on the tick.</summary>
    [Test]
    public async Task CoveringStanceCoversNeighbours()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = 0, during = 0, far = 0;
        EntityUid hero = default, near = default, distant = default;
        await Server.WaitPost(() =>
        {
            hero = Spawn();
            GiveFeat(hero, "CFCoveringStance");
            near = Spawn(x: 1);
            distant = Spawn(x: 5);
        });

        await Server.WaitRunTicks(40);
        await Server.WaitPost(() =>
        {
            before = Bonuses.Total(near, CFBonusStat.ArmorClass);
            Actions.PerformAction(hero, Action(hero, "CFActCoveringStance"), new CFActivityInstantEvent());
        });

        await Server.WaitRunTicks(40);
        await Server.WaitPost(() =>
        {
            during = Bonuses.Total(near, CFBonusStat.ArmorClass);
            far = Bonuses.Total(distant, CFBonusStat.ArmorClass);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(0));
            Assert.That(during, Is.EqualTo(1));
            Assert.That(far, Is.EqualTo(0));
        });
    }

    /// <summary>Shield Your Eyes turns a critical failure against a visual effect into a failure, with the shield up.</summary>
    [Test]
    public async Task ShieldYourEyesSoftensACriticalFailure()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        CFDegree lowered = CFDegree.CriticalFailure, raised = CFDegree.CriticalFailure, other = CFDegree.CriticalFailure;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFShieldYourEyes");
            var shield = Spawn("CFSteelShield");
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, shield, animate: false);
            var visual = new System.Collections.Generic.HashSet<ProtoId<Content.Shared.CF.Traits.CFTraitPrototype>> { "Visual" };
            lowered = Bonuses.UpgradeSave(hero, CFDefenseType.Fortitude, CFDegree.CriticalFailure, visual);
            SEntMan.System<CFShieldSystem>().SetRaised((shield, SEntMan.GetComponent<CFShieldComponent>(shield)), true);
            raised = Bonuses.UpgradeSave(hero, CFDefenseType.Fortitude, CFDegree.CriticalFailure, visual);
            other = Bonuses.UpgradeSave(hero, CFDefenseType.Fortitude, CFDegree.CriticalFailure, new() { "Fire" });
        });

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.EqualTo(CFDegree.CriticalFailure), "The shield is down.");
            Assert.That(raised, Is.EqualTo(CFDegree.Failure));
            Assert.That(other, Is.EqualTo(CFDegree.CriticalFailure), "Not a visual effect.");
        });
    }

    /// <summary>Raise Haft gives +2 with a parry weapon, +1 otherwise.</summary>
    [Test]
    public async Task RaiseHaftIsBetterWithAParryWeapon()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int plain = 0, parry = 0;
        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var effects = SEntMan.System<CFEffectSystem>();
            var hero = Spawn();
            GiveFeat(hero, "CFRaiseHaft");
            var action = Action(hero, "CFActRaiseHaft");
            hands.TryPickupAnyHand(hero, Spawn("CFGreatsword"), animate: false);
            effects.ResolveTarget(action, hero, hero);
            plain = Bonuses.Total(hero, CFBonusStat.ArmorClass);

            var other = Spawn(x: 2);
            GiveFeat(other, "CFRaiseHaft");
            var sword = Spawn("CFGreatsword", 2);
            SEntMan.GetComponent<Content.Shared.CF.Traits.CFTraitComponent>(sword).Traits.Add("Parry");
            hands.TryPickupAnyHand(other, sword, animate: false);
            effects.ResolveTarget(Action(other, "CFActRaiseHaft"), other, other);
            parry = Bonuses.Total(other, CFBonusStat.ArmorClass);
        });

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(1));
            Assert.That(parry, Is.EqualTo(2));
        });
    }

    /// <summary>Shielded Attrition spends the reactions of the enemies about.</summary>
    [Test]
    public async Task ShieldedAttritionSpendsEnemyReactions()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int near = 0, far = 0;
        var raised = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFShieldedAttrition");
            var shield = Spawn("CFSteelShield");
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(hero, shield, animate: false);
            var close = Spawn("CFGuardianFoe", 1);
            var distant = Spawn("CFGuardianFoe", 6);
            Actions.PerformAction(hero, Action(hero, "CFActShieldedAttrition"), new CFActivityInstantEvent());
            near = SEntMan.GetComponent<CFCombatOptionsComponent>(close).ReactionSpent;
            far = SEntMan.GetComponent<CFCombatOptionsComponent>(distant).ReactionSpent;
            raised = SEntMan.GetComponent<CFShieldComponent>(shield).Raised;
        });

        Assert.Multiple(() =>
        {
            Assert.That(near, Is.Not.EqualTo(0));
            Assert.That(far, Is.EqualTo(0));
            Assert.That(raised, Is.True);
        });
    }

    /// <summary>Flying Tackle rushes at the enemy and trips it: Strength 40 against a plain Reflex DC never fails.</summary>
    [Test]
    public async Task FlyingTackleTripsTheFoe()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var prone = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeat(hero, "CFFlyingTackle");
            var foe = Spawn("CFGuardianFoe", 3);
            Actions.PerformAction(hero, Action(hero, "CFActFlyingTackle"), new CFActivityTargetEvent { Target = foe });
            prone = SEntMan.System<CFConditionSystem>().IsProne(foe);
        });

        Assert.That(prone, Is.True);
    }

    /// <summary>Every Guardian feat applies.</summary>
    [Test]
    public async Task EveryGuardianFeatCanBeApplied()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitAssertion(() =>
        {
            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>().Where(f => f.Class == "Guardian"))
            {
                Features.Apply(Spawn(), feat.Grants, feat.ID);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(23));
    }
}
