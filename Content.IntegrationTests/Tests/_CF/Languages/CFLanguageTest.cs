using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.Chat.Systems;
using Content.Shared.CF.Character;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Preferences;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF.Languages;

/// <summary>What nonsense sounds like. Plain math, so no game runs.</summary>
[TestFixture]
public sealed class CFLanguageGarbleTest
{
    private static readonly string[] Syllables = { "gob", "nak", "zug" };

    [Test]
    public void AWordAlwaysSoundsTheSame()
    {
        Assert.That(CFLanguageRules.Garble(Syllables, "stand down"), Is.EqualTo(CFLanguageRules.Garble(Syllables, "stand down")));
    }

    [Test]
    public void ItHidesTheWordsAndKeepsTheShape()
    {
        var garbled = CFLanguageRules.Garble(Syllables, "Stand down, friend!");
        Assert.Multiple(() =>
        {
            Assert.That(garbled, Does.Not.Contain("tand"));
            Assert.That(garbled, Does.Not.Contain("down"));
            Assert.That(garbled, Does.EndWith("!"));
            Assert.That(garbled, Does.Contain(", "));
            Assert.That(char.IsUpper(garbled[0]), "A capital stays a capital.");
        });
    }

    [Test]
    public void ALanguageWithNoSoundsLeavesItAlone()
    {
        Assert.That(CFLanguageRules.Garble(new List<string>(), "hello"), Is.EqualTo("hello"));
    }
}

/// <summary>Who knows what, and that a real listener hears the right thing.</summary>
[TestFixture]
[TestOf(typeof(CFLanguageSystem))]
public sealed class CFLanguageTest : GameTest
{
    private const string Line = "Stand down friend";

    private static readonly Box2i Footprint = new(-1, -1, 3, 2);

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private CFDevMap _map = default!;

    private static Dictionary<CFAbility, int> Intelligence(int value)
    {
        return new Dictionary<CFAbility, int> { { CFAbility.Intelligence, value } };
    }

    private static List<ProtoId<CFLanguagePrototype>> Picks(params string[] ids)
    {
        return ids.Select(id => new ProtoId<CFLanguagePrototype>(id)).ToList();
    }

    [Test]
    public async Task EveryAncestryLanguageExists()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var ancestry in Server.ProtoMan.EnumeratePrototypes<CFAncestryPrototype>())
            {
                Assert.That(ancestry.Languages, Is.Not.Empty, ancestry.ID);
                foreach (var language in ancestry.Languages)
                {
                    Assert.That(Server.ProtoMan.HasIndex(language), $"{ancestry.ID} speaks {language}.");
                }
            }
        });
    }

    [Test]
    public async Task IntelligenceLimitsWhatYouCanPick()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ProtoMan;
            var human = prototypes.EnumeratePrototypes<CFAncestryPrototype>().First(a => a.ID == "Human");
            var picks = Picks("Goblin", "Dwarven", "Elven");

            Assert.Multiple(() =>
            {
                Assert.That(CFLanguageRules.Known(prototypes, human, picks, Intelligence(0)), Is.EquivalentTo(human.Languages));
                Assert.That(CFLanguageRules.Known(prototypes, human, picks, Intelligence(-1)), Is.EquivalentTo(human.Languages));

                var two = CFLanguageRules.Known(prototypes, human, picks, Intelligence(2));
                Assert.That(two, Has.Count.EqualTo(human.Languages.Count + 2));
                Assert.That(two, Does.Contain(new ProtoId<CFLanguagePrototype>("Goblin")));
                Assert.That(two, Does.Not.Contain(new ProtoId<CFLanguagePrototype>("Elven")));
            });
        });
    }

    [Test]
    public async Task AModifiedClientCannotPickARareLanguage()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ProtoMan;
            var human = prototypes.EnumeratePrototypes<CFAncestryPrototype>().First(a => a.ID == "Human");
            var known = CFLanguageRules.Known(prototypes, human, Picks("Androffan", "NoSuchLanguage"), Intelligence(4));
            Assert.That(known, Is.EquivalentTo(human.Languages));
        });
    }

    [Test]
    public async Task YouCanOnlySpeakWhatYouKnow()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<CFLanguageSystem>();
            var mob = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
            var languages = SEntMan.AddComponent<CFLanguagesComponent>(mob);
            languages.Known = Picks("Common", "Goblin");

            Assert.That(system.TryGetSpoken(mob, out var first));
            Assert.That(first!.ID, Is.EqualTo("Common"), "Common comes first when nothing was chosen.");

            Assert.That(system.TrySpeak((mob, languages), "Elven"), Is.False);
            Assert.That(system.TrySpeak((mob, languages), "Goblin"));
            Assert.That(system.TryGetSpoken(mob, out var spoken));
            Assert.That(spoken!.ID, Is.EqualTo("Goblin"));
        });
    }

    /// <summary>The whole way: one creature speaks, and the client's own chat history shows what its player heard.</summary>
    [TestCase(InGameICChatType.Speak)]
    [TestCase(InGameICChatType.Whisper)]
    public async Task AListenerOnlyUnderstandsALanguageTheyKnow(InGameICChatType type)
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid speaker = default;
        CFLanguagesComponent hearing = default!;

        await Server.WaitPost(() =>
        {
            speaker = SEntMan.SpawnEntity("MobHuman", _map.Local(0, 0));
            var speech = SEntMan.AddComponent<CFLanguagesComponent>(speaker);
            speech.Known = Picks("Goblin");
            speech.Speaking = "Goblin";

            var listener = SEntMan.SpawnEntity("MobHuman", _map.Local(1, 0));
            hearing = SEntMan.AddComponent<CFLanguagesComponent>(listener);
            hearing.Known = Picks("Common");
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, listener);
        });

        await Pair.RunTicksSync(10);

        var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var before = chat.History.Count;
        await Server.WaitPost(() =>
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(speaker, Line, type, false, ignoreActionBlocker: true));
        await Pair.RunTicksSync(10);

        Assert.That(chat.History, Has.Count.GreaterThan(before), "The listener heard something.");
        var garbled = chat.History[^1].Msg;
        Assert.Multiple(() =>
        {
            Assert.That(garbled.Message, Does.Not.Contain("friend"));
            Assert.That(garbled.WrappedMessage, Does.Not.Contain("friend"));
            Assert.That(garbled.WrappedMessage, Does.Not.Contain("Goblin"), "It cannot tell what language this is.");
        });

        await Server.WaitPost(() => hearing.Known.Add("Goblin"));
        before = chat.History.Count;
        await Server.WaitPost(() =>
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(speaker, Line, type, false, ignoreActionBlocker: true));
        await Pair.RunTicksSync(10);

        Assert.That(chat.History, Has.Count.GreaterThan(before));
        var understood = chat.History[^1].Msg;
        Assert.Multiple(() =>
        {
            Assert.That(understood.WrappedMessage, Does.Contain("friend"));
            Assert.That(understood.WrappedMessage, Does.Contain("Goblin"), "It says which language it is in.");
        });
    }
}
