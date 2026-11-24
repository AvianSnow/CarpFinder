using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Afflictions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Scent;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>The right-click skill actions: Steal, Perform, Subsist, Track, Cover Tracks, Treat Poison and Repair.</summary>
[TestFixture]
[TestOf(typeof(CFSkillActionSystem))]
public sealed class CFSkillActionTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSkillActionTestHero
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
  parent: CFSkillActionTestHero
  id: CFSkillActionTestFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFSkillActionSystem Skills => SEntMan.System<CFSkillActionSystem>();

    /// <summary>A hero who cannot fail, or with <paramref name="modifier"/> negative cannot succeed, at anything.</summary>
    private EntityUid Hero(int x, int modifier = Extreme)
    {
        var hero = SEntMan.SpawnEntity("CFSkillActionTestHero", _map.Local(x, 0));
        var scores = SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers;
        foreach (var ability in new[] { CFAbility.Strength, CFAbility.Dexterity, CFAbility.Constitution, CFAbility.Intelligence, CFAbility.Wisdom, CFAbility.Charisma })
        {
            scores[ability] = modifier;
        }

        return hero;
    }

    [Test]
    public async Task StealTakesWhatIsInAPocket()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(0);
            var foe = SEntMan.SpawnEntity("CFSkillActionTestFoe", _map.Local(1, 0));
            var loot = SEntMan.SpawnEntity("CFRation", _map.Local(1, 0));
            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.TryEquip(foe, loot, "pocket1", silent: true, force: true));

            Assert.That(Skills.TrySteal(hero, foe));
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(hero, loot), "The thief has it in hand.");
            Assert.That(inventory.TryGetSlotEntity(foe, "pocket1", out _), Is.False);
            Assert.That(SEntMan.GetComponent<CFActionPoolComponent>(hero).Stamina, Is.EqualTo(2f), "Steal is 1 action.");

            var ally = Hero(2);
            Assert.That(Skills.TrySteal(hero, ally), Is.False, "Nobody picks a friend's pocket.");
        });
    }

    [Test]
    public async Task PerformSubsistAndTrackFollowTheirChecks()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(0);
            var clumsy = Hero(2, -Extreme);

            // A natural 1 makes a critical success a success, so none of these ask for the critical.
            Assert.That(Skills.TryPerform(hero)?.IsSuccess(), Is.True);

            Assert.That(Skills.Subsist(hero).IsSuccess());
            var held = 0;
            foreach (var item in SEntMan.System<SharedHandsSystem>().EnumerateHeld(hero))
            {
                if (SEntMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == "CFRation")
                    held++;
            }

            Assert.That(held, Is.InRange(1, 2), "A success finds a ration, a critical success two.");

            // Forager turns the worst result into a success.
            SEntMan.System<CFCharacterFeatureSystem>().SetOption(clumsy, CFCombatOption.Forager);
            Assert.That(Skills.Subsist(clumsy), Is.EqualTo(CFDegree.Success));

            Assert.That(SEntMan.HasComponent<CFSmellerComponent>(hero), Is.False);
            Assert.That(Skills.Track(hero).IsSuccess());
            Assert.That(SEntMan.GetComponent<CFSmellerComponent>(hero).Sniffing, "A tracker sees the trails.");
            Assert.That(Skills.Track(clumsy).IsSuccess(), Is.False);
            Assert.That(SEntMan.HasComponent<CFSmellerComponent>(clumsy), Is.False);
        });
    }

    [Test]
    public async Task TreatPoisonHelpsTheNextSaveAndRepairMendsAShield()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(0);
            var patient = Hero(1);

            // A creature with no class saves with its printed number: this low, the venom always takes hold.
            SEntMan.GetComponent<CFCombatantComponent>(patient).PrintedFortitude = -Extreme;
            var afflictions = SEntMan.System<CFAfflictionSystem>();
            afflictions.Expose(patient, "HuntingSpiderVenom");
            Assert.That(afflictions.Has(patient, CFConditionKind.Poisoned));

            Assert.That(Skills.Treat(hero, patient, CFConditionKind.Poisoned)?.IsSuccess(), Is.True);
            Assert.That(SEntMan.GetComponent<CFAfflictionsComponent>(patient).Active[0].TreatBonus, Is.InRange(2, 4));
            Assert.That(Skills.Treat(hero, hero, CFConditionKind.Poisoned), Is.Null, "Nothing to treat on a healthy creature.");

            var shieldUid = SEntMan.SpawnEntity("CFSteelShield", _map.Local(0, 0));
            var shield = SEntMan.GetComponent<CFShieldComponent>(shieldUid);
            shield.Hp = 4;
            Skills.Repair(hero, (shieldUid, shield));
            Assert.That(shield.CurrentHp, Is.AnyOf(9, 14), "An untrained crafter restores 5, or 10 on a critical success.");

            var clumsy = Hero(2, -Extreme);
            shield.Hp = 20;
            Skills.Repair(clumsy, (shieldUid, shield));
            Assert.That(shield.CurrentHp, Is.LessThanOrEqualTo(20), "A critical failure never mends.");
        });
    }

    [Test]
    public async Task CoveringTracksHalvesSpeed()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(0);
            var speed = SEntMan.GetComponent<MovementSpeedModifierComponent>(hero);
            var before = speed.CurrentWalkSpeed;

            // The verb asks for training in Survival. The component is what halves the Speed and stops the trail.
            var skills = SEntMan.EnsureComponent<CFSkillActionsComponent>(hero);
            skills.CoveringTracks = true;
            SEntMan.System<Content.Shared.Movement.Systems.MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(hero);
            Assert.That(speed.CurrentWalkSpeed, Is.EqualTo(before * CFSharedSkillActionSystem.CoverTracksSpeed).Within(0.01f));
        });
    }
}
