using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.CF.ZLevels.Core;
using Content.Shared.Alert;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle;
using Content.Shared.CF.Alerts;
using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Alerts;

/// <summary>
/// Sleep, Wake and Look up are buttons on the alerts panel, beside the temperature and pressure icons, not actions on
/// the hotbar. Each shows only while it has something to do, and clicking it does what the action did.
/// </summary>
[TestFixture]
[TestOf(typeof(CFRestAlertSystem))]
[TestOf(typeof(CFSharedZLevelsSystem))]
public sealed class CFAlertTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFAlertHero
  components:
  - type: CFCombatant
    level: 1
    maxHp: 20
    currentHp: 20
  - type: CFAbilityScore
";

    private AlertsSystem Alerts => SEntMan.System<AlertsSystem>();

    /// <summary>Whether this alert, and not another of its category, is the one shown.</summary>
    private bool Showing(EntityUid uid, ProtoId<AlertPrototype> alert)
    {
        Assert.That(Alerts.TryGet(alert, out var proto), $"{alert} is not an alert.");
        return Alerts.TryGetAlertState(uid, proto!.AlertKey, out var state) && state.Type == alert;
    }

    /// <summary>What the panel does when the button is clicked.</summary>
    private bool Click(EntityUid uid, ProtoId<AlertPrototype> alert)
    {
        Assert.That(Alerts.TryGet(alert, out var proto), $"{alert} is not an alert.");
        return Alerts.ActivateAlert(uid, proto!);
    }

    [Test]
    public async Task SleepAndWakeAreButtonsOnTheAlertsPanel()
    {
        var map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("CFAlertHero", map.Local(0, 0));
            var bed = SEntMan.SpawnEntity("CFWoodenBed", map.Local(0, 0));
            var buckle = SEntMan.System<SharedBuckleSystem>();

            Assert.That(Showing(hero, CFRestAlertSystem.SleepAlert), Is.False, "Nothing to sleep in yet.");

            Assert.That(buckle.TryBuckle(hero, hero, bed));
            Assert.That(Showing(hero, CFRestAlertSystem.SleepAlert), "In a bed, Sleep is offered.");

            Assert.That(Click(hero, CFRestAlertSystem.SleepAlert), "Clicking Sleep works.");
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<SleepingComponent>(hero), "They are asleep.");
                Assert.That(Showing(hero, CFRestAlertSystem.WakeAlert), "Asleep, Wake is offered.");
                Assert.That(Showing(hero, CFRestAlertSystem.SleepAlert), Is.False, "Sleep is gone while asleep.");
            });

            Assert.That(Click(hero, CFRestAlertSystem.WakeAlert), "Clicking Wake works.");
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<SleepingComponent>(hero), Is.False, "They are awake.");
                Assert.That(Showing(hero, CFRestAlertSystem.SleepAlert), "Still in the bed, Sleep is back.");
            });

            buckle.Unbuckle(hero, hero);
            Assert.That(Alerts.IsShowingAlertCategory(hero, CFRestAlertSystem.Category), Is.False, "Out of bed, neither shows.");
        });
    }

    [Test]
    public async Task LookUpIsAButtonOnTheAlertsPanelWhereThereIsALevelAbove()
    {
        await Server.WaitAssertion(() =>
        {
            var maps = SEntMan.System<SharedMapSystem>();
            var levels = SEntMan.System<CFZLevelsSystem>();
            var low = Level(maps);
            var high = Level(maps);

            var network = levels.CreateMapNetwork();
            var depths = new Dictionary<EntityUid, int>
            {
                [maps.GetMapOrInvalid(low.Map)] = 0,
                [maps.GetMapOrInvalid(high.Map)] = 1,
            };
            Assert.That(levels.TryAddMapsIntoNetwork(network, depths));

            var hero = SEntMan.SpawnAtPosition("CFAlertHero", maps.GridTileToLocal(low.Grid, low.Comp, Vector2i.Zero));
            var viewer = SEntMan.EnsureComponent<CFZLevelViewerComponent>(hero);
            Assert.Multiple(() =>
            {
                Assert.That(viewer.ActionEntity, Is.Null, "A body with an alerts panel gets no action.");
                Assert.That(Showing(hero, CFSharedZLevelsSystem.LookUpAlert), "With a level above, Look up is offered.");
            });

            Assert.That(Click(hero, CFSharedZLevelsSystem.LookUpAlert), "Clicking Look up works.");
            Assert.Multiple(() =>
            {
                Assert.That(viewer.LookUp, "They look up.");
                Assert.That(Showing(hero, CFSharedZLevelsSystem.LookDownAlert), "Looking up, the button looks back down.");
                Assert.That(Showing(hero, CFSharedZLevelsSystem.LookUpAlert), Is.False);
            });

            Assert.That(Click(hero, CFSharedZLevelsSystem.LookDownAlert));
            Assert.Multiple(() =>
            {
                Assert.That(viewer.LookUp, Is.False, "They look back down.");
                Assert.That(Showing(hero, CFSharedZLevelsSystem.LookUpAlert));
            });

            var top = SEntMan.SpawnAtPosition("CFAlertHero", maps.GridTileToLocal(high.Grid, high.Comp, Vector2i.Zero));
            SEntMan.EnsureComponent<CFZLevelViewerComponent>(top);
            Assert.That(Alerts.IsShowingAlertCategory(top, CFSharedZLevelsSystem.LookAlertCategory), Is.False,
                "On the top level there is nothing to look up at.");
        });
    }

    /// <summary>A level with a small square of floor.</summary>
    private (MapId Map, EntityUid Grid, Robust.Shared.Map.Components.MapGridComponent Comp) Level(SharedMapSystem maps)
    {
        maps.CreateMap(out var id);
        var grid = maps.CreateGridEntity(id);
        var plating = Server.ResolveDependency<ITileDefinitionManager>()["Plating"];
        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(plating.TileId));
            }
        }

        return (id, grid.Owner, grid.Comp);
    }
}
