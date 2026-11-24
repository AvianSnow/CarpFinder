using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Afflictions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Traits;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Confused, controlled, fleeing, restrained, slowed and quickened, immunities, and a poison's stages.</summary>
[TestFixture]
[TestOf(typeof(CFConditionSystem))]
public sealed class CFConditionRulesTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFConditionRulesTestHero
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 200
    currentHp: 200
  - type: CFAbilityScore
  - type: CFCombatOptions
  - type: CFFaction
    faction: Alpha

- type: entity
  parent: CFConditionRulesTestHero
  id: CFConditionRulesTestFoe
  components:
  - type: CFFaction
    faction: Monster

- type: entity
  parent: CFConditionRulesTestFoe
  id: CFConditionRulesTestImmune
  components:
  - type: CFConditionImmunity
    conditions: [ Confused ]
    traits: [ Poison ]
    criticalHits: true
    precision: true
";

    private CFDevMap _map = default!;

    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();
    private CFFactionSystem Factions => SEntMan.System<CFFactionSystem>();
    private CFAfflictionSystem Afflictions => SEntMan.System<CFAfflictionSystem>();

    private EntityUid Spawn(string id, int x, int y = 0)
    {
        return SEntMan.SpawnEntity(id, _map.Local(x, y));
    }

    private int MaxActions(EntityUid uid)
    {
        return SEntMan.GetComponent<CFActionPoolComponent>(uid).MaxActions;
    }

    [Test]
    public async Task SlowedTakesActionsAndQuickenedGivesOne()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFConditionRulesTestHero", 0);
            Assert.That(MaxActions(hero), Is.EqualTo(3));

            Conditions.ApplyCondition(hero, CFConditionKind.Slowed, 2, 60f, hero, 0);
            Assert.That(MaxActions(hero), Is.EqualTo(1));

            Conditions.ApplyCondition(hero, CFConditionKind.Quickened, 1, 60f, hero, 0);
            Assert.That(MaxActions(hero), Is.EqualTo(2), "Slowed 2 and quickened leave 2.");

            Assert.That(Conditions.EndCondition(hero, CFConditionKind.Slowed));
            Assert.That(MaxActions(hero), Is.EqualTo(4));

            Assert.That(Conditions.EndCondition(hero, CFConditionKind.Quickened));
            Assert.That(MaxActions(hero), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task RestrainedAndFleeingCreaturesCannotAttack()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFConditionRulesTestHero", 0);
            var foe = Spawn("CFConditionRulesTestFoe", 1);
            var item = SEntMan.SpawnEntity("CFLongsword", _map.Local(0, 0));
            SEntMan.System<CFWeaponSystem>().TryStats(item, out var sword);
            var strikes = SEntMan.System<CFStrikeSystem>();
            SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Strength] = Extreme;
            var foeStats = SEntMan.GetComponent<CFCombatantComponent>(foe);

            Conditions.ApplyCondition(hero, CFConditionKind.Restrained, 1, 60f, foe, 0);
            Assert.Multiple(() =>
            {
                Assert.That(Conditions.CannotActHostile(hero));
                Assert.That(Conditions.CannotCast(hero));
                Assert.That(Conditions.IsOffGuardTo(hero, foe), "Restrained is off-guard.");
                Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(hero).CurrentWalkSpeed, Is.Zero, "Restrained cannot move.");
            });

            strikes.PerformStrike(hero, foe, sword, item, melee: true);
            Assert.That(foeStats.CurrentHp, Is.EqualTo(200), "A restrained creature's Strike does not happen.");

            Conditions.EndCondition(hero, CFConditionKind.Restrained);
            Assert.That(Conditions.CannotActHostile(hero), Is.False);
            Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(hero).CurrentWalkSpeed, Is.GreaterThan(0f));

            Conditions.ApplyCondition(hero, CFConditionKind.Fleeing, 1, 60f, foe, 0);
            Assert.That(Conditions.CannotActHostile(hero) && Conditions.CannotCast(hero), "Fleeing only moves.");
            strikes.PerformStrike(hero, foe, sword, item, melee: true);
            Assert.That(foeStats.CurrentHp, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task ConfusedHasNoAlliesAndControlledChangesSides()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFConditionRulesTestHero", 0);
            var ally = Spawn("CFConditionRulesTestHero", 1);
            var foe = Spawn("CFConditionRulesTestFoe", 2);
            var otherFoe = Spawn("CFConditionRulesTestFoe", 3);
            Assert.That(Factions.CanHarm(hero, ally), Is.False);

            Conditions.ApplyCondition(hero, CFConditionKind.Confused, 1, 60f, foe, 0);
            Assert.Multiple(() =>
            {
                Assert.That(Factions.CanHarm(hero, ally), "A confused creature has no allies.");
                Assert.That(Conditions.IsOffGuardTo(hero, foe));
                Assert.That(SEntMan.GetComponent<CFCombatOptionsComponent>(hero).ReactionSpent, Is.Not.Zero, "It has no reactions.");
            });
            Conditions.EndCondition(hero, CFConditionKind.Confused);
            Assert.That(Factions.CanHarm(hero, ally), Is.False);

            Conditions.ApplyCondition(foe, CFConditionKind.Controlled, 1, 60f, hero, 0);
            Assert.Multiple(() =>
            {
                Assert.That(Factions.CanHarm(foe, hero), Is.False, "It will not harm its controller.");
                Assert.That(Factions.CanHarm(foe, ally), Is.False, "Nor its controller's allies.");
                Assert.That(Factions.CanHarm(ally, foe), Is.False);
                Assert.That(Factions.CanHarm(foe, otherFoe), "It fights its old side.");
                Assert.That(Factions.GetAlliance(foe, hero), Is.EqualTo(CFAlliance.Party));
            });

            Conditions.EndCondition(foe, CFConditionKind.Controlled);
            Assert.That(Factions.CanHarm(foe, hero), "Control ends and it is an enemy again.");
            Assert.That(Factions.CanHarm(foe, otherFoe), Is.False);
        });
    }

    [Test]
    public async Task ImmunitiesRuleOutConditionsTraitsAndCriticalHits()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Spawn("CFConditionRulesTestHero", 0);
            var immune = Spawn("CFConditionRulesTestImmune", 1);
            SEntMan.GetComponent<CFAbilityScoreComponent>(immune).AbilityModifiers[CFAbility.Constitution] = -Extreme;

            Conditions.ApplyCondition(immune, CFConditionKind.Confused, 1, 60f, hero, 0);
            Assert.Multiple(() =>
            {
                Assert.That(Conditions.IsConfused(immune), Is.False);
                Assert.That(Conditions.IsImmuneToTraits(immune, new HashSet<ProtoId<CFTraitPrototype>> { "Poison" }));
                Assert.That(Conditions.IsImmuneToTraits(immune, new HashSet<ProtoId<CFTraitPrototype>> { "Fire" }), Is.False);
                Assert.That(Conditions.ImmuneToCriticalHits(immune));
                Assert.That(Conditions.ImmuneToPrecision(immune));
                Assert.That(Conditions.ImmuneToCriticalHits(hero), Is.False);
            });

            Afflictions.Expose(immune, "HuntingSpiderVenom");
            Assert.That(Afflictions.Stage(immune, "HuntingSpiderVenom"), Is.Null, "Poison never takes hold of a creature immune to it.");
        });
    }

    [Test]
    public async Task AVenomRunsItsStagesAndEndsAtItsMaximumDuration()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hero = default(EntityUid);
        await Server.WaitAssertion(() =>
        {
            hero = Spawn("CFConditionRulesTestHero", 0);

            // A Fortitude so low that every save fails.
            SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Constitution] = -Extreme;
            Afflictions.Expose(hero, "HuntingSpiderVenom");
            Assert.That(Afflictions.Stage(hero, "HuntingSpiderVenom"), Is.Zero, "It has taken hold.");
            Assert.That(Afflictions.Has(hero, CFConditionKind.Poisoned));
        });

        // Two seconds: the first stage has bitten.
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Afflictions.Stage(hero, "HuntingSpiderVenom"), Is.GreaterThan(0));
                Assert.That(Conditions.Value(hero, CFConditionKind.Poisoned), Is.GreaterThan(0), "The HUD's marker follows the stage.");
                Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.LessThan(200), "The stage dealt its poison damage.");
            });
        });

        // Six rounds is 36 seconds: by 40 it has run its course whatever the saves.
        await Pair.RunTicksSync(1200);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Afflictions.Stage(hero, "HuntingSpiderVenom"), Is.Null);
            Assert.That(Conditions.Value(hero, CFConditionKind.Poisoned), Is.Zero);
            Assert.That(SEntMan.GetComponent<CFCombatantComponent>(hero).CurrentHp, Is.GreaterThan(0));
        });
    }
}
