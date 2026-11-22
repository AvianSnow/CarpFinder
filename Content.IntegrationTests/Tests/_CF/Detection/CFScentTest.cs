using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.CF.Character;
using Content.Server.CF.Scent;
using Content.Shared.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Scent;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Detection;

/// <summary>Scent trails, after Starlight: every creature leaves marks, and a creature with the Scent sense can sniff them out.</summary>
[TestFixture]
[TestOf(typeof(CFScentServerSystem))]
public sealed class CFScentTest : GameTest
{
    private static readonly Box2i Footprint = new(-1, -1, 2, 2);

    public override PoolSettings PoolSettings => new() { Connected = false };

    private CFDevMap _map = default!;

    private int Marks(string scentId)
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<CFScentMarkerComponent>();
        while (query.MoveNext(out _, out var marker))
        {
            if (marker.ScentId == scentId)
                count++;
        }

        return count;
    }

    [Test]
    public async Task ACreatureLeavesATrailAndStandingStillPoolsIt()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hero = default(EntityUid);
        var scentId = string.Empty;
        await Server.WaitPost(() =>
        {
            hero = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithCFBuild(new CFCharacterBuildData());
            SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, profile);
            scentId = SEntMan.GetComponent<CFScentComponent>(hero).ScentId ?? string.Empty;
        });

        // About 4 seconds: two or three emissions, all merged into one mark because the hero has not moved.
        await Pair.RunTicksSync(120);
        var marks = 0;
        var strength = 0f;
        await Server.WaitPost(() =>
        {
            marks = Marks(scentId);
            var query = SEntMan.AllEntityQueryEnumerator<CFScentMarkerComponent>();
            while (query.MoveNext(out _, out var marker))
            {
                if (marker.ScentId == scentId)
                    strength = marker.Strength;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(scentId, Is.Not.Empty, "A character gets a scent of its own at spawn.");
            Assert.That(marks, Is.EqualTo(1), "Standing still pools one mark.");
            Assert.That(strength, Is.GreaterThan(0f), "Each emission strengthens it.");
        });
    }

    [Test]
    public async Task TheScentSenseGivesSniffAndItTogglesTheView()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var hasAction = false;
        var sniffingBefore = true;
        var sniffingAfter = false;
        var plainHasSmeller = true;
        await Server.WaitPost(() =>
        {
            var species = Server.ProtoMan.Index(new ProtoId<CFAncestryPrototype>("Ratfolk")).Species;
            var rat = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
            SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(rat, HumanoidCharacterProfile.DefaultWithSpecies(species).WithCFBuild(new CFCharacterBuildData { Heritage = "LongsnoutRat" }));
            var smeller = SEntMan.GetComponent<CFSmellerComponent>(rat);
            hasAction = SEntMan.System<SharedActionsSystem>().GetActions(rat)
                .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "CFActSniff");
            sniffingBefore = smeller.Sniffing;
            SEntMan.System<CFScentServerSystem>().SetSniffing((rat, smeller), true);
            sniffingAfter = smeller.Sniffing;

            var plain = SEntMan.SpawnEntity("MobHuman", _map.Local(1, 0));
            SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(plain, HumanoidCharacterProfile.DefaultWithSpecies(species).WithCFBuild(new CFCharacterBuildData { Heritage = "DesertRat" }));
            plainHasSmeller = SEntMan.HasComponent<CFSmellerComponent>(plain);
        });

        Assert.Multiple(() =>
        {
            Assert.That(hasAction, Is.True);
            Assert.That(sniffingBefore, Is.False);
            Assert.That(sniffingAfter, Is.True);
            Assert.That(plainHasSmeller, Is.False, "Only the Scent sense smells.");
        });
    }

    [Test]
    public async Task AWolfSmellsAndLeavesScent()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        var smells = false;
        var scented = false;
        await Server.WaitPost(() =>
        {
            var wolf = SEntMan.SpawnEntity("CFMobWolf", _map.Local(0, 0));
            smells = SEntMan.HasComponent<CFSmellerComponent>(wolf);
            scented = SEntMan.HasComponent<CFScentComponent>(wolf);
        });

        Assert.Multiple(() =>
        {
            Assert.That(smells, Is.True);
            Assert.That(scented, Is.True);
        });
    }
}
