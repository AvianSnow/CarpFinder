using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Detection;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.CF.Combat;

/// <summary>Remaster Hide, Sneak and Seek.</summary>
[TestFixture]
[TestOf(typeof(CFStealthSystem))]
public sealed class CFStealthTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 8, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFStealthHero
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
  parent: CFStealthHero
  id: CFStealthFoe
  components:
  - type: CFFaction
    faction: Monster
";

    private CFDevMap _map = default!;

    private CFStealthSystem Stealth => SEntMan.System<CFStealthSystem>();
    private CFConditionSystem Conditions => SEntMan.System<CFConditionSystem>();
    private CFActionPoolSystem Actions => SEntMan.System<CFActionPoolSystem>();

    private EntityUid Hero(int dexterity = Extreme)
    {
        var hero = SEntMan.SpawnEntity("CFStealthHero", _map.Local(0, 0));
        SEntMan.GetComponent<CFAbilityScoreComponent>(hero).AbilityModifiers[CFAbility.Dexterity] = dexterity;
        return hero;
    }

    private EntityUid Foe(int x = 2)
    {
        return SEntMan.SpawnEntity("CFStealthFoe", _map.Local(x, 0));
    }

    private EntityUid Wall(int x = 1)
    {
        return SEntMan.SpawnAtPosition("WallSolid", _map.Local(x, 0));
    }

    private float Stamina(EntityUid mob)
    {
        return SEntMan.GetComponent<CFActionPoolComponent>(mob).Stamina;
    }

    private CFStealthComponent Sneaking(EntityUid hero, EntityUid foe, CFDetection detection)
    {
        var stealth = SEntMan.EnsureComponent<CFStealthComponent>(hero);
        stealth.Active = true;
        stealth.Detection[foe] = detection;
        return stealth;
    }

    [Test]
    public async Task HidingNeedsCover()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe();

            Assert.That(Stealth.CoverBonus(hero, foe), Is.Zero);
            Assert.That(Stealth.TryHide(hero), Is.False, "Nothing to hide behind.");
            Assert.That(Stealth.IsUnseenBy(hero, foe), Is.False);
            Assert.That(Stamina(hero), Is.EqualTo(2f), "The action is still spent.");

            var wall = Wall();
            Assert.That(Stealth.IsCover(wall));
            Assert.That(Stealth.CoverBonus(hero, foe), Is.EqualTo(4), "A whole wall is greater cover.");
            Assert.That(Stealth.TryHide(hero));
            Assert.That(Stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Hidden));
        });
    }

    [Test]
    public async Task SneakResultsFollowTheDegree()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe();
            Wall();
            Sneaking(hero, foe, CFDetection.Hidden);

            Assert.That(Stealth.TrySneak(hero));
            Assert.That(Stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Undetected));
            Assert.That(Stamina(hero), Is.EqualTo(2f));

            var clumsy = Hero(dexterity: -Extreme);
            Sneaking(clumsy, foe, CFDetection.Undetected);
            Assert.That(Stealth.TrySneak(clumsy));
            Assert.That(Stealth.Detection(clumsy, foe), Is.Not.EqualTo(CFDetection.Undetected));
        });
    }

    [Test]
    public async Task SneakingInTheOpenIsSeenWithNoRoll()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe();
            Sneaking(hero, foe, CFDetection.Undetected);

            Assert.That(Stealth.TrySneak(hero));
            Assert.That(Stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Observed));
        });
    }

    [Test]
    public async Task ACreatureThatSawYouKeepsSeeingYou()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe();
            Wall();
            Sneaking(hero, foe, CFDetection.Observed);

            Assert.That(Stealth.TrySneak(hero));
            Assert.That(Stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Observed));
        });
    }

    [Test]
    public async Task AnyOtherActionReveals()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe();
            var stealth = Sneaking(hero, foe, CFDetection.Undetected);

            Assert.That(Actions.TrySpendActions(hero, 1, stealthy: true));
            Assert.That(stealth.Active, "Hide and Sneak do not count.");

            Assert.That(Actions.TrySpendActions(hero, 1));
            Assert.That(stealth.Active, Is.False);
            Assert.That(Stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Observed));
        });
    }

    [Test]
    public async Task NoiseGivesAwayThePositionOnly()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe();
            Sneaking(hero, foe, CFDetection.Undetected);

            Stealth.MakeNoise(hero);
            Assert.That(Stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Hidden));
        });
    }

    [Test]
    public async Task AStrikeFindsTheTargetOffGuardThenReveals()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe(1);
            var strikes = SEntMan.System<CFStrikeSystem>();
            var stealth = Sneaking(hero, foe, CFDetection.Undetected);

            Assert.That(strikes.TryMeleeWeapon(hero, out var stats, out var weapon));
            strikes.PerformStrike(hero, foe, stats, weapon, melee: true);

            Assert.That(Conditions.IsOffGuardTo(foe, hero));
            Assert.That(stealth.Active, Is.False);
        });
    }

    [Test]
    public async Task AttackingAHiddenCreatureNeedsAFlatCheck()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe();
            Sneaking(hero, foe, CFDetection.Hidden);

            var results = new HashSet<bool>();
            for (var i = 0; i < 200; i++)
            {
                results.Add(Conditions.PassFlatCheck(foe, hero));
            }

            Assert.That(results, Is.EquivalentTo(new[] { true, false }));
        });
    }

    [Test]
    public async Task SeekStepsTheDetectionDown()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(dexterity: 0);
            var foe = Foe();
            SEntMan.GetComponent<CFCombatantComponent>(foe).PrintedPerception = Extreme;
            Sneaking(hero, foe, CFDetection.Hidden);

            var spot = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(hero);
            Assert.That(Stealth.TrySeek(foe, spot));
            Assert.That(Stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Observed));
            Assert.That(Stamina(foe), Is.EqualTo(2f));

            Sneaking(hero, foe, CFDetection.Undetected);
            Assert.That(Stealth.TrySeek(foe, spot));
            Assert.That(Stealth.Detection(hero, foe), Is.Not.EqualTo(CFDetection.Undetected));
        });
    }

    [Test]
    public async Task NpcsCannotTargetAnUndetectedCreature()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe();
            var factions = SEntMan.System<CFFactionSystem>();

            var seen = new HashSet<EntityUid> { hero };
            factions.ApplyHostileQuery(foe, 10f, seen);
            Assert.That(seen, Does.Contain(hero));

            Sneaking(hero, foe, CFDetection.Hidden);
            factions.ApplyHostileQuery(foe, 10f, seen);
            Assert.That(seen, Does.Contain(hero), "A hidden creature's square is known.");

            Sneaking(hero, foe, CFDetection.Undetected);
            factions.ApplyHostileQuery(foe, 10f, seen);
            Assert.That(seen, Does.Not.Contain(hero));
        });
    }

    [Test]
    public async Task ClosingAClosetHidesWhoIsInside()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var foe = Foe(3);
            var closet = SEntMan.SpawnEntity("ClosetTool", _map.Local(1, 0));
            var storage = SEntMan.System<SharedEntityStorageSystem>();

            Assert.That(storage.Insert(hero, closet));
            var closed = new StorageAfterCloseEvent(null);
            SEntMan.EventBus.RaiseLocalEvent(closet, ref closed);
            Assert.That(Stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Hidden));
            Assert.That(Stamina(hero), Is.EqualTo(3f), "Getting in was the action.");

            storage.OpenStorage(closet);
            Assert.That(Stealth.Detection(hero, foe), Is.EqualTo(CFDetection.Observed));
        });
    }

    /// <summary>
    /// A deleted observer used to stay in the networked dictionary. Two of them both became an invalid entity when the
    /// state was sent, and the duplicate key broke the game state of every player who could see the hider.
    /// </summary>
    [Test]
    public async Task DeletedObserversAreForgotten()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero();
            var first = Foe(2);
            var second = Foe(3);
            var stealth = Sneaking(hero, first, CFDetection.Hidden);
            stealth.Detection[second] = CFDetection.Observed;

            SEntMan.DeleteEntity(first);
            SEntMan.DeleteEntity(second);

            Assert.That(stealth.Detection, Is.Empty);
            Assert.DoesNotThrow(() => SEntMan.GetNetEntityDictionary(stealth.Detection));
        });
    }
}
