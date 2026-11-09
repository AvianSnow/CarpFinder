using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Client.CF.Character;
using Content.Client.CF.Lobby.UI.CharacterBuilder;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared.CF;
using Content.Shared.CF.Character;
using Content.Shared.CF.Items;
using Content.Shared.CF.Preferences;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.CF.Shop;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Station.Systems;
using Robust.Client.Player;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>What a character needs before it can join, buying several of an item, and what it wears.</summary>
[TestFixture]
[TestOf(typeof(CFCharacterRequirements))]
public sealed class CFCharacterSetupTest : GameTest
{
    private const string Job = "CFAlpha";
    private const string Potion = "CFLoadoutHealingPotion";
    private const string PotionItem = "CFHealingPotionMinor";

    public override PoolSettings PoolSettings => new() { Connected = true, DummyTicker = false, InLobby = true };

    private IPrototypeManager Prototypes => Server.ProtoMan;

    private HumanoidCharacterProfile Complete()
    {
        var build = new CFCharacterBuildData
        {
            Background = Prototypes.EnumeratePrototypes<CFBackgroundPrototype>().First().ID,
            Class = Prototypes.EnumeratePrototypes<CFClassPrototype>().First().ID,
        };
        return new HumanoidCharacterProfile().WithCFBuild(build).WithJobPriority(Job, JobPriority.High);
    }

    private HumanoidCharacterProfile WithPotions(int copies)
    {
        var profile = Complete();
        var role = new RoleLoadout("JobCFAlpha");
        role.SelectedLoadouts["CFGearBasics"] = new List<Loadout> { new() { Prototype = Potion } };
        var build = profile.CFBuild.Clone();
        build.LoadoutCopies[Potion] = copies;
        return profile.WithLoadout(role).WithCFBuild(build);
    }

    private int PotionPrice => CFCoins.EntityPrice(Prototypes.Index<EntityPrototype>(PotionItem));

    [Test]
    public async Task ANewCharacterStillNeedsABackgroundClassAndJob()
    {
        List<LocId> missing = default!;
        await Server.WaitPost(() => missing = CFCharacterRequirements.Missing(Prototypes, new HumanoidCharacterProfile()));

        Assert.That(missing.Select(id => id.Id), Is.EqualTo(new[]
        {
            "cf-character-missing-background",
            "cf-character-missing-class",
            "cf-character-missing-job",
        }));
    }

    [Test]
    public async Task ACharacterWithAllFourIsComplete()
    {
        var count = -1;
        await Server.WaitPost(() => count = CFCharacterRequirements.Missing(Prototypes, Complete()).Count);

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task AJobSetToNeverIsNotAChoice()
    {
        List<LocId> missing = default!;
        await Server.WaitPost(() => missing = CFCharacterRequirements.Missing(Prototypes, Complete().WithJobPriority(Job, JobPriority.Never)));

        Assert.That(missing.Select(id => id.Id), Is.EqualTo(new[] { "cf-character-missing-job" }));
    }

    [Test]
    public async Task OnlyAFinishedCharacterCanReadyUp()
    {
        var ticker = SEntMan.System<ServerGameTicker>();
        var prefs = Server.ResolveDependency<IServerPreferencesManager>();
        var session = Pair.Player!;
        var before = PlayerGameStatus.ReadyToPlay;
        var after = PlayerGameStatus.NotReadyToPlay;

        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(CFCVars.RequireCompleteCharacter, true);
            ticker.ToggleReady(session, true);
            before = ticker.PlayerGameStatuses[session.UserId];
            prefs.SetProfile(session.UserId, 0, Complete()).Wait();
            ticker.ToggleReady(session, true);
            after = ticker.PlayerGameStatuses[session.UserId];
            ticker.ToggleReady(session, false);
            Server.CfgMan.SetCVar(CFCVars.RequireCompleteCharacter, false);
        });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(PlayerGameStatus.NotReadyToPlay));
            Assert.That(after, Is.EqualTo(PlayerGameStatus.ReadyToPlay));
        });
    }

    [Test]
    public async Task ExtraCopiesAreChargedAndGiven()
    {
        List<(string Item, int Count)> copies = default!;
        int left = 0, remaining = 0, expected = 0;
        await Server.WaitPost(() =>
        {
            var profile = WithPotions(2);
            copies = CFCoins.AffordableCopies(profile, Job, Prototypes, out left);
            remaining = CFCoins.Remaining(profile, Job, Prototypes);
            expected = CFCoins.StartingCopper(1) - PotionPrice * 3;
        });

        Assert.Multiple(() =>
        {
            Assert.That(copies, Is.EqualTo(new[] { (PotionItem, 2) }));
            Assert.That(left, Is.EqualTo(expected));
            Assert.That(remaining, Is.EqualTo(expected), "The shop's purse and the one at spawn agree.");
        });
    }

    [Test]
    public async Task ACheatedCartIsCutToWhatThePurseHolds()
    {
        List<(string Item, int Count)> copies = default!;
        int left = -1, expected = 0;
        await Server.WaitPost(() =>
        {
            copies = CFCoins.AffordableCopies(WithPotions(int.MaxValue), Job, Prototypes, out left);
            expected = Math.Min(CFCoins.MaxCopies, (CFCoins.StartingCopper(1) - PotionPrice) / PotionPrice);
        });

        Assert.Multiple(() =>
        {
            Assert.That(copies.Single().Count, Is.EqualTo(expected));
            Assert.That(left, Is.GreaterThanOrEqualTo(0));
        });
    }

    [Test]
    public async Task TheSetupShopListsWhatTheJobCanBuy()
    {
        HumanoidCharacterProfile profile = default!;
        await Server.WaitPost(() => profile = Complete());
        var listed = 0;
        await Client.WaitPost(() =>
        {
            var tab = new BuilderShopTab();
            tab.Rebuild(
                profile,
                null,
                Client.ResolveDependency<IPlayerManager>().LocalSession,
                IoCManager.Instance,
                Client.ProtoMan,
                () => profile,
                _ => { });
            listed = tab.Children.OfType<CFShopView>().Single().ListingCount;
        });

        Assert.That(listed, Is.GreaterThan(0));
    }

    [Test]
    public async Task ExtraCopiesSurviveBeingSaved()
    {
        CFCharacterBuildData build = default!, loaded = default!;
        await Server.WaitPost(() =>
        {
            var serialization = Server.ResolveDependency<ISerializationManager>();
            build = new CFCharacterBuildData();
            build.LoadoutCopies[Potion] = 4;

            // The database stores the build as JSON, which is the step that has lost data before.
            var json = serialization.WriteValue(build, notNullableOverride: true).ToJsonNode();
            loaded = serialization.Read<CFCharacterBuildData>(json.ToDataNode(), notNullableOverride: true);
        });

        Assert.That(loaded, Is.EqualTo(build));
    }

    [Test]
    public async Task EveryJobDressesItsCharacter()
    {
        var map = await CFDevMap.Load(Pair, new Box2i(-1, -1, 2, 2));
        var worn = new List<string?>();
        var undressed = new List<string>();
        await Server.WaitPost(() =>
        {
            foreach (var group in Prototypes.EnumeratePrototypes<CFJobGroupPrototype>())
            {
                foreach (var job in group.Roles)
                {
                    if (Prototypes.Index(job).StartingGear == null)
                        undressed.Add(job);
                }
            }

            var mob = SEntMan.SpawnEntity("MobHuman", map.Local(0, 0));
            SEntMan.System<StationSpawningSystem>().EquipStartingGear(mob, new ProtoId<StartingGearPrototype>("CFCommonClothes"));
            var inventory = SEntMan.System<InventorySystem>();
            foreach (var slot in new[] { "jumpsuit", "shoes", "back" })
            {
                inventory.TryGetSlotEntity(mob, slot, out var item);
                worn.Add(item == null ? null : SEntMan.GetComponent<MetaDataComponent>(item.Value).EntityPrototype?.ID);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(undressed, Is.Empty);
            Assert.That(worn, Is.EqualTo(new[] { "CFClothingCommonOutfit", "CFClothingCommonShoes", "CFAdventurersKit" }));
        });
    }

    [Test]
    public async Task TheAdventurersKitIsPackedAndCanBeCarried()
    {
        var map = await CFDevMap.Load(Pair, new Box2i(-1, -1, 2, 2));
        var packed = new List<string?>();
        int tenths = 0, water = 0, nutriment = 0;
        await Server.WaitPost(() =>
        {
            var mob = SEntMan.SpawnEntity("MobHuman", map.Local(0, 0));
            SEntMan.System<StationSpawningSystem>().EquipStartingGear(mob, new ProtoId<StartingGearPrototype>("CFCommonClothes"));
            SEntMan.System<InventorySystem>().TryGetSlotEntity(mob, "back", out var kit);

            var containers = SEntMan.System<SharedContainerSystem>();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            foreach (var item in containers.GetContainer(kit!.Value, "storagebase").ContainedEntities)
            {
                packed.Add(SEntMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID);
                foreach (var (_, solution) in solutions.EnumerateSolutions(item))
                {
                    water += solution.Comp.Solution.GetTotalPrototypeQuantity("Water").Int();
                    nutriment += solution.Comp.Solution.GetTotalPrototypeQuantity("Nutriment").Int();
                }
            }

            tenths = SEntMan.System<CFEncumbranceSystem>().CarriedTenths(mob);
        });

        Assert.Multiple(() =>
        {
            Assert.That(packed, Is.EquivalentTo(new[] { "CFRope", "CFTorch", "CFLighter", "CFRation", "CFCanteenWater" }));
            Assert.That(water, Is.EqualTo(60));
            Assert.That(nutriment, Is.EqualTo(20));
            Assert.That(tenths, Is.LessThanOrEqualTo(20), "A new character has room left for armor.");
        });
    }
}
