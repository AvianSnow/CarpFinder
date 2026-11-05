using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Scene;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Scene;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Verbs;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.CF.Scene;

/// <summary>Narrate and skill check triggers, with a real connected player.</summary>
[TestFixture]
[TestOf(typeof(CFSceneSystem))]
public sealed class CFSceneTest : GameTest
{
    private const int Extreme = 60;

    private static readonly Box2i Footprint = new(-1, -1, 12, 2);

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: CFSceneTestHero
  components:
  - type: CFCombatant
    level: 1
  - type: CFAbilityScore
";

    private CFDevMap _map = default!;

    private EntityUid Hero(int x)
    {
        var mob = SEntMan.SpawnEntity("CFSceneTestHero", _map.Local(x, 0));
        Server.PlayerMan.SetAttachedEntity(ServerSession!, mob);
        return mob;
    }

    private Entity<CFNarateComponent> Narrate(int x, string text = "You are in a forest")
    {
        var uid = SEntMan.SpawnEntity("CFNarateTrigger", _map.Local(x, 0));
        var comp = SEntMan.GetComponent<CFNarateComponent>(uid);
        comp.Text = text;
        return (uid, comp);
    }

    private Entity<CFSkillCheckComponent> Check(string skills = "Perception")
    {
        var uid = SEntMan.SpawnEntity("CFSkillCheckTrigger", _map.Local(0, 0));
        var comp = SEntMan.GetComponent<CFSkillCheckComponent>(uid);
        comp.Skills = skills;
        return (uid, comp);
    }

    private void Link(EntityUid source, EntityUid door, params string[] ports)
    {
        var links = new List<DeviceLink>();
        foreach (var port in ports)
        {
            links.Add(new DeviceLink(port, "Open"));
        }

        SEntMan.System<DeviceLinkSystem>().SaveLinks(null, source, door, links);
    }

    [Test]
    public async Task PlayerHearsItOnceInRangeAndNotBefore()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Entity<CFNarateComponent> trigger = default;
        EntityUid hero = default;

        await Server.WaitPost(() =>
        {
            trigger = Narrate(0);
            hero = Hero(8);
        });
        await Pair.RunSeconds(1);
        await Server.WaitAssertion(() => Assert.That(trigger.Comp.Heard, Is.Empty, "40 feet is beyond the default 15."));

        await Server.WaitPost(() =>
            SEntMan.System<SharedTransformSystem>().SetCoordinates(hero, _map.Local(2, 0)));
        await Pair.RunSeconds(1);
        await Server.WaitAssertion(() => Assert.That(trigger.Comp.Heard, Does.Contain(ServerSession!.UserId)));

        await Server.WaitAssertion(() =>
        {
            var again = SEntMan.System<CFSceneSystem>().Narrate(trigger, ServerSession!, hero);
            Assert.That(again, Is.False, "It only triggers once for a player.");
        });
    }

    [Test]
    public async Task MobsWithoutAPlayerDoNotTriggerIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Entity<CFNarateComponent> trigger = default;

        await Server.WaitPost(() =>
        {
            trigger = Narrate(0);
            Hero(8);
            SEntMan.SpawnEntity("CFMobGiantRat", _map.Local(1, 0));
        });
        await Pair.RunSeconds(1);

        await Server.WaitAssertion(() => Assert.That(trigger.Comp.Heard, Is.Empty));
    }

    [Test]
    public async Task RadiusEditedLiveTakesEffect()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        Entity<CFNarateComponent> trigger = default;

        await Server.WaitPost(() =>
        {
            trigger = Narrate(0);
            Hero(8);
            trigger.Comp.RadiusFeet = 60f;
        });
        await Pair.RunSeconds(1);

        await Server.WaitAssertion(() => Assert.That(trigger.Comp.Heard, Has.Count.EqualTo(1)));
    }

    [Test]
    public async Task NarrationSignalOpensALinkedDoor()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid door = default;

        await Server.WaitPost(() =>
        {
            var trigger = Narrate(0);
            door = SEntMan.SpawnEntity("BlastDoor", _map.Local(5, 0));
            Link(trigger, door, "CFNarated");
            Hero(1);
        });
        await Pair.RunSeconds(2);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<DoorComponent>(door).State, Is.Not.EqualTo(DoorState.Closed)));
    }

    [Test]
    public async Task EachEntityKeepsItsOwnText()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var first = Narrate(0, "A rickety bridge spans the passage");
            var second = SEntMan.GetComponent<CFNarateComponent>(SEntMan.SpawnEntity("CFNarateTrigger", _map.Local(1, 0)));
            Assert.That(second.Text, Is.Empty);
            Assert.That(first.Comp.Text, Is.EqualTo("A rickety bridge spans the passage"));

            var check = Check();
            check.Comp.Success = "You find a loose stone.";
            var other = SEntMan.GetComponent<CFSkillCheckComponent>(SEntMan.SpawnEntity("CFSkillCheckTrigger", _map.Local(1, 0)));
            Assert.That(other.Success, Is.Empty);
        });
    }

    /// <summary>
    /// A bare map, not the dev map: the dev map cannot be saved without errors of its own,
    /// because of a stock package that points at a missing entity.
    /// </summary>
    [Test]
    public async Task TextSavesWithTheMap()
    {
        var path = new ResPath("/cf_scene_test.yml");

        await Server.WaitAssertion(() =>
        {
            var maps = SEntMan.System<SharedMapSystem>();
            var mapUid = maps.CreateMap(out var mapId, runMapInit: false);
            var grid = maps.CreateGridEntity(mapId);
            var plating = Server.ResolveDependency<ITileDefinitionManager>()["Plating"];
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(plating.TileId));
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.One, new Tile(plating.TileId));

            var narrate = SEntMan.SpawnAtPosition("CFNarateTrigger", maps.GridTileToLocal(grid.Owner, grid.Comp, Vector2i.Zero));
            var narrateComp = SEntMan.GetComponent<CFNarateComponent>(narrate);
            narrateComp.Text = "You are in a forest";
            narrateComp.RadiusFeet = 25f;

            var check = SEntMan.SpawnAtPosition("CFSkillCheckTrigger", maps.GridTileToLocal(grid.Owner, grid.Comp, Vector2i.One));
            var checkComp = SEntMan.GetComponent<CFSkillCheckComponent>(check);
            checkComp.Skills = "Athletics, Stealth";
            checkComp.Dc = 22;
            checkComp.CriticalFailure = "The floor gives way!";

            var loader = SEntMan.System<MapLoaderSystem>();
            Assert.That(loader.TrySaveMap(mapId, path));
            SEntMan.DeleteEntity(mapUid);
            Assert.That(loader.TryLoadMap(path, out var loaded, out _));

            // A map just loaded is paused, so these use the queries that include paused entities.
            var foundNarrate = false;
            var narrates = SEntMan.AllEntityQueryEnumerator<CFNarateComponent, TransformComponent>();
            while (narrates.MoveNext(out var saved, out var xform))
            {
                foundNarrate |= xform.MapUid == loaded!.Value.Owner
                                && saved.Text == "You are in a forest"
                                && saved.RadiusFeet == 25f;
            }

            var foundCheck = false;
            var checks = SEntMan.AllEntityQueryEnumerator<CFSkillCheckComponent, TransformComponent>();
            while (checks.MoveNext(out var saved, out var xform))
            {
                foundCheck |= xform.MapUid == loaded!.Value.Owner
                              && saved.Skills == "Athletics, Stealth"
                              && saved.Dc == 22
                              && saved.CriticalFailure == "The floor gives way!";
            }

            Assert.That(foundNarrate, "The narration should survive a save and load.");
            Assert.That(foundCheck, "The skill check should survive a save and load.");
        });
    }

    [Test]
    public async Task SkillListIsForgivingAboutSpelling()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var skills = SEntMan.System<CFSceneSystem>().SkillsOf(Check("athletics,  Stealth, perception, Nonsense").Comp);
            Assert.That(skills, Is.EqualTo(new[] { "Athletics", "Stealth", "Perception" }));
        });
    }

    [Test]
    public async Task RightClickOffersEachSkillUntilRolled()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(1);
            var check = Check("Athletics, Stealth");
            var verbs = SEntMan.System<SharedVerbSystem>();

            var offered = new List<string>();
            foreach (var verb in verbs.GetLocalVerbs(check, hero, typeof(Verb)))
            {
                if (verb.Text is "Athletics" or "Stealth")
                    offered.Add(verb.Text);
            }

            Assert.That(offered, Is.EquivalentTo(new[] { "Athletics", "Stealth" }));

            SEntMan.System<CFSceneSystem>().Roll(check, hero, ServerSession!, "Stealth");

            foreach (var verb in verbs.GetLocalVerbs(check, hero, typeof(Verb)))
            {
                Assert.That(verb.Text, Is.Not.AnyOf("Athletics", "Stealth"), "No second attempt.");
            }
        });
    }

    [Test]
    public async Task RollPicksTheDegreeAndOnlyOnce()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var hero = Hero(1);
            SEntMan.GetComponent<CFCombatantComponent>(hero).PrintedPerception = Extreme;
            var scene = SEntMan.System<CFSceneSystem>();
            var check = Check();

            var degree = scene.Roll(check, hero, ServerSession!, "Perception");
            Assert.That(degree, Is.Not.Null);
            Assert.That(degree!.Value.IsSuccess());
            Assert.That(scene.Roll(check, hero, ServerSession!, "Perception"), Is.Null, "One attempt each.");

            check.Comp.OncePerPlayer = false;
            Assert.That(scene.Roll(check, hero, ServerSession!, "Perception"), Is.Not.Null);
        });
    }

    [Test]
    public async Task FailureSignalFiresOnlyOnFailure()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid onFailure = default;
        EntityUid onSuccess = default;

        await Server.WaitPost(() =>
        {
            var hero = Hero(1);
            SEntMan.GetComponent<CFCombatantComponent>(hero).PrintedPerception = -Extreme;
            var check = Check();
            check.Comp.OncePerPlayer = false;

            onFailure = SEntMan.SpawnEntity("BlastDoor", _map.Local(5, 0));
            onSuccess = SEntMan.SpawnEntity("BlastDoor", _map.Local(7, 0));
            Link(check, onFailure, "CFSkillCriticalFailure", "CFSkillFailure");
            Link(check, onSuccess, "CFSkillSuccess", "CFSkillCriticalSuccess");

            SEntMan.System<CFSceneSystem>().Roll(check, hero, ServerSession!, "Perception");
        });
        await Pair.RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<DoorComponent>(onFailure).State, Is.Not.EqualTo(DoorState.Closed));
            Assert.That(SEntMan.GetComponent<DoorComponent>(onSuccess).State, Is.EqualTo(DoorState.Closed));
        });
    }

    [Test]
    public async Task ComponentAddedToAnyObjectGetsItsSignals()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var statue = SEntMan.SpawnEntity("CFHoodedLantern", _map.Local(1, 0));
            SEntMan.EnsureComponent<CFSkillCheckComponent>(statue);
            var source = SEntMan.GetComponent<DeviceLinkSourceComponent>(statue);
            Assert.That(source.Ports, Has.Count.EqualTo(4));
        });
    }
}
