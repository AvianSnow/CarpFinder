using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Shared.CF.Afflictions;
using Content.Shared.CF.Combat;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Afflictions;

/// <summary>
/// Staged afflictions, run on the printed Alcohol entry. Fortitude is extreme, so a natural 1 or 20 cannot change
/// whether the save passes. A failure can still be a critical failure, so stage checks allow for that.
/// </summary>
[TestFixture]
[TestOf(typeof(CFAfflictionSystem))]
public sealed class CFAfflictionTest : GameTest
{
    private const string Alcohol = "Alcohol";

    private static readonly Box2i Footprint = new(-1, -1, 4, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFAfflictionTestWeak
  components:
  - type: CFCombatant
    level: 1
    armorClass: 10
    maxHp: 50
    currentHp: 50
    printedFortitude: -60
  - type: CFAbilityScore

- type: entity
  parent: CFAfflictionTestWeak
  id: CFAfflictionTestStrong
  components:
  - type: CFCombatant
    printedFortitude: 60
";

    private CFAfflictionSystem Afflictions => SEntMan.System<CFAfflictionSystem>();

    private CFActiveAffliction Active(EntityUid mob)
    {
        return SEntMan.GetComponent<CFAfflictionsComponent>(mob).Active.Single();
    }

    private void Drink(EntityUid mob)
    {
        if (SEntMan.TryGetComponent(mob, out CFAfflictionsComponent? comp) && comp.Active.Count > 0)
            comp.Active[0].NextExposure = TimeSpan.Zero;

        Afflictions.Expose(mob, Alcohol);
    }

    /// <summary>Makes the next onset or save due, and lets the system run it.</summary>
    private async Task Tick(EntityUid mob)
    {
        await Server.WaitPost(() => Active(mob).NextTick = TimeSpan.Zero);
        await Pair.RunTicksSync(2);
    }

    [Test]
    public async Task PassingTheExposureSaveDoesNothing()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitPost(() =>
        {
            var mob = SEntMan.SpawnEntity("CFAfflictionTestStrong", map.Local(1, 0));
            Afflictions.Expose(mob, Alcohol);
            Assert.That(Afflictions.Stage(mob, Alcohol), Is.Null);
        });
    }

    [Test]
    public async Task FailingStartsTheOnsetThenTheFirstStages()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity("CFAfflictionTestWeak", map.Local(1, 0));
            Afflictions.Expose(mob, Alcohol);
            Assert.That(Afflictions.Stage(mob, Alcohol), Is.EqualTo(0));
        });

        await Tick(mob);
        await Server.WaitAssertion(() => Assert.That(Afflictions.Stage(mob, Alcohol), Is.InRange(1, 2)));
    }

    [Test]
    public async Task FailingLaterSavesDoesNotRaiseTheStageButDrinkingDoes()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity("CFAfflictionTestWeak", map.Local(1, 0));
            Afflictions.Expose(mob, Alcohol);
        });
        await Tick(mob);

        var stage = 0;
        await Server.WaitPost(() => stage = Active(mob).Stage);
        await Tick(mob);
        await Tick(mob);
        await Server.WaitPost(() =>
        {
            Assert.That(Active(mob).Stage, Is.EqualTo(stage));
            Drink(mob);
            Assert.That(Active(mob).Stage, Is.GreaterThan(stage));
        });
    }

    [Test]
    public async Task ACooldownStopsOneDrinkCountingTwice()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity("CFAfflictionTestWeak", map.Local(1, 0));
            Afflictions.Expose(mob, Alcohol);
        });
        await Tick(mob);
        await Server.WaitPost(() =>
        {
            var stage = Active(mob).Stage;
            Drink(mob);
            var after = Active(mob).Stage;
            Assert.That(after, Is.GreaterThan(stage));
            Afflictions.Expose(mob, Alcohol);
            Assert.That(Active(mob).Stage, Is.EqualTo(after));
        });
    }

    [Test]
    public async Task HeavyDrinkingGivesItsConditions()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity("CFAfflictionTestWeak", map.Local(1, 0));
            Afflictions.Expose(mob, Alcohol);
        });
        await Tick(mob);
        await Server.WaitPost(() =>
        {
            while (Active(mob).Stage < 3)
                Drink(mob);

            var conditions = SEntMan.System<CFConditionSystem>();
            Assert.That(Active(mob).Stage, Is.InRange(3, 5));
            Assert.That(conditions.Clumsy(mob), Is.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public async Task TheLastStageKills()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity("CFAfflictionTestWeak", map.Local(1, 0));
            Afflictions.Expose(mob, Alcohol);
        });
        await Tick(mob);
        await Server.WaitPost(() =>
        {
            while (Active(mob).Stage < 7)
                Drink(mob);

            Assert.That(SEntMan.System<MobStateSystem>().IsDead(mob));
        });

        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() => Assert.That(Afflictions.Stage(mob, Alcohol), Is.Null));
    }

    [Test]
    public async Task EveryDrinkExposes()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "CFBeer", "CFWine", "CFAle", "CFDwarfBeer", "CFMagicBeer" })
            {
                var reagent = Server.ProtoMan.Index<ReagentPrototype>(id);
                var effects = reagent.Metabolisms!.Metabolisms.Values.SelectMany(entry => entry.Effects);
                Assert.That(effects.OfType<CFExposeAffliction>().Any(), id);
            }
        });
    }
}
