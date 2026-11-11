using Robust.Shared.Prototypes;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Preferences;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>An Inventor's innovation, on the real data.</summary>
[TestFixture]
[TestOf(typeof(CFInventorSystem))]
public sealed class CFInnovationTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 3, 2);
    private const int StartHp = 500;

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFInnovationHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 500
    currentHp: 500
  - type: CFAbilityScore
    abilityModifiers:
      Intelligence: 40
  - type: CFCombatOptions
  - type: CFActionPool
    maxActions: 30
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFInnovationHero
  id: CFInnovationFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFCharacterFeatureSystem Features => SEntMan.System<CFCharacterFeatureSystem>();

    private CFInventorSystem Inventor => SEntMan.System<CFInventorSystem>();

    private SharedActionsSystem Actions => SEntMan.System<SharedActionsSystem>();

    private EntityUid Spawn(string id = "CFInnovationHero", int x = 0) => SEntMan.SpawnEntity(id, _map.Local(x, 0));

    private void GiveFeature(EntityUid hero, string name)
    {
        var feature = Server.ProtoMan.Index(new ProtoId<CFClassPrototype>("Inventor")).Features.First(f => f.Name == name);
        Features.Apply(hero, feature.Grants, name);
    }

    private void Choose(EntityUid hero, string id) => Features.Apply(hero, Server.ProtoMan.Index<CFClassChoicePrototype>(id).Grants, id);

    private Entity<ActionComponent> Action(EntityUid hero, string id)
    {
        return Actions.GetActions(hero).First(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == id);
    }

    private void Wear(EntityUid hero, string id)
    {
        Assert.That(SEntMan.System<InventorySystem>().TryEquip(hero, Spawn(id), "outerClothing", force: true), Is.True);
    }

    private int Hurt(EntityUid mob) => StartHp - SEntMan.GetComponent<CFCombatantComponent>(mob).CurrentHp;

    /// <summary>Intelligence 40 against a low DC means success, or a critical success, all but always.</summary>
    [Test]
    public async Task OverdriveAddsDamageAndThenWaits()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int before = -1, during = 0;
        var again = true;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Overdrive");
            before = Inventor.OverdriveDamage(hero);
            var action = Action(hero, "CFActOverdrive");
            Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            during = Inventor.OverdriveDamage(hero);
            SEntMan.GetComponent<CFOverdriveComponent>(hero).Until = System.TimeSpan.Zero;
            var attempt = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref attempt);
            again = !attempt.Cancelled;
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(0));
            Assert.That(during, Is.AnyOf(20, 40));
            Assert.That(again, Is.False, "It has to cool down.");
        });
    }

    [Test]
    public async Task ExplodeNeedsTheInnovationAndHurtsEnemiesNearIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        bool without = true, with = false;
        var most = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            GiveFeature(hero, "Explode");
            var action = Action(hero, "CFActExplode");
            var first = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref first);
            without = !first.Cancelled;

            Wear(hero, "CFInnovationPowerSuit");
            var second = new ActionAttemptEvent(hero, null);
            SEntMan.EventBus.RaiseLocalEvent(action, ref second);
            with = !second.Cancelled;

            var foe = Spawn("CFInnovationFoe", 1);
            for (var i = 0; i < 30; i++)
            {
                SEntMan.EnsureComponent<CFOverdriveComponent>(hero).UnstableDownUntil = System.TimeSpan.Zero;
                Actions.PerformAction(hero, action, new CFActivityInstantEvent());
            }

            most = Hurt(foe);
        });

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.False);
            Assert.That(with, Is.True);
            Assert.That(most, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task AFailedUnstableCheckStopsTheInnovation()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var stopped = false;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            Wear(hero, "CFInnovationPowerSuit");
            for (var i = 0; i < 200 && !stopped; i++)
            {
                Inventor.RollUnstable(hero);
                stopped = !Inventor.CanUseUnstable(hero);
            }
        });

        Assert.That(stopped, Is.True);
    }

    [Test]
    public async Task ArmorResistanceCountsOnlyWhileWorn()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        int bare = 0, worn = 0, other = 0;
        await Server.WaitPost(() =>
        {
            var hero = Spawn();
            var bonuses = SEntMan.System<CFBonusSystem>();
            Choose(hero, "ModArmorHarmonicOscillator");
            bare = bonuses.ModifyDamage(hero, "Sonic", 10);
            Wear(hero, "CFInnovationPowerSuit");
            worn = bonuses.ModifyDamage(hero, "Sonic", 10);
            other = bonuses.ModifyDamage(hero, "Fire", 10);
        });

        Assert.Multiple(() =>
        {
            Assert.That(bare, Is.EqualTo(10));
            Assert.That(worn, Is.EqualTo(7), "3 + half of level 1, rounded down.");
            Assert.That(other, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task EveryOptionCanBeAppliedAndEveryItemIsAnInnovation()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var count = 0;
        await Server.WaitAssertion(() =>
        {
            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>().Where(c => c.Class.Id == "Inventor"))
            {
                Features.Apply(Spawn(), choice.Grants, choice.ID);
                count++;
            }

            foreach (var id in new[] { "CFInnovationPowerSuit", "CFInnovationSubterfugeSuit", "CFInnovationWeaponModular", "CFInnovationWeaponEntangling", "CFInnovationWeaponHefty", "CFInnovationWeaponHampering", "CFInnovationWeaponWeighted", "CFInnovationWeaponPacifying", "CFCompanionConstruct" })
            {
                Assert.That(SEntMan.HasComponent<CFInnovationComponent>(Spawn(id)), Is.True, id);
            }
        });

        Assert.That(count, Is.EqualTo(17));
    }

    [Test]
    public async Task TheConstructHasThePrototypeStatistics()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var pet = Server.ProtoMan.Index(new ProtoId<CFPetPrototype>("CFPetConstruct"));
            var block = CFPetMath.Present(pet, new CFCharacterBuildData { Class = "Inventor" }, null);
            Assert.That(block.ArmorClass, Is.EqualTo(16), "10 + level 1 + Dexterity 3 + trained 2.");
            Assert.That(block.HitPoints, Is.EqualTo(18), "10 + 6 + Constitution 2, at level 1.");
        });
    }

    [Test]
    public async Task TheConstructIsNotAliveAndRollsLikeAPlayer()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var construct = Spawn("CFCompanionConstruct");
            var combat = SEntMan.GetComponent<CFCombatantComponent>(construct);
            var effects = SEntMan.System<CFEffectSystem>();
            var bonuses = SEntMan.System<CFBonusSystem>();

            combat.CurrentHp = 5;
            effects.Heal(construct, construct, 10);
            Assert.That(combat.CurrentHp, Is.EqualTo(5), "Healing does not work on it.");
            Assert.That(bonuses.ModifyDamage(construct, "Poison", 50), Is.EqualTo(0));
            Assert.That(bonuses.ModifyDamage(construct, "Slashing", 50), Is.EqualTo(50));
            Assert.That(combat.ProficiencyRanks["Athletics"], Is.EqualTo(CFProficiencyRank.Trained));
            Assert.That(SEntMan.GetComponent<CFAbilityScoreComponent>(construct).GetModifier(CFAbility.Dexterity), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task AConstructInnovationIsAConstructCompanion()
    {
        await Server.WaitAssertion(() =>
        {
            var build = new CFCharacterBuildData { Class = "Inventor" };
            Assert.That(CFPetMath.BuildGrants(Server.ProtoMan, build, CFPetKind.Construct), Is.False);
            build.ClassChoices.Add("InnovationConstruct");
            Assert.That(CFPetMath.BuildGrants(Server.ProtoMan, build, CFPetKind.Construct), Is.True);
        });
    }
}
