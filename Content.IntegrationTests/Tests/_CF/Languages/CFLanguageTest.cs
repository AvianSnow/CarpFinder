using System.Collections.Generic;
using System.Linq;
using Content.Client.CF.Languages;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.CF;
using Content.Server.Chat.Systems;
using Content.Server.CF.Character;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.Preferences;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Preferences;
using Content.Shared.Chat;
using Content.Shared.Speech.Components;
using Robust.Client.UserInterface.Controls;
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
        var first = CFLanguageRules.Garble(Syllables, "stand down");
        Assert.That(CFLanguageRules.Garble(Syllables, "stand down"), Is.EqualTo(first));
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
    public async Task ALevelFiveIntelligenceBoostTeachesTheNextPick()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ProtoMan;
            var human = prototypes.EnumeratePrototypes<CFAncestryPrototype>().First(a => a.ID == "Human");
            var picks = Picks("Goblin", "Dwarven");
            var boosts = new[] { CFAbility.Intelligence, CFAbility.Strength };

            Assert.Multiple(() =>
            {
                Assert.That(CFLanguageRules.LevelFiveLanguage(Intelligence(1), boosts));
                Assert.That(CFLanguageRules.LevelFiveLanguage(Intelligence(4), boosts), Is.False, "At +4 a boost changes nothing.");
                Assert.That(CFLanguageRules.LevelFiveLanguage(Intelligence(1), new[] { CFAbility.Strength }), Is.False);
                Assert.That(CFLanguageRules.LevelFivePick(prototypes, human, picks, Intelligence(1))?.Id, Is.EqualTo("Dwarven"));
            });
        });
    }

    [Test]
    public async Task AnimalsGrowlUndeadMoanAndKoboldsKeepTheirOwnTongue()
    {
        var map = await CFDevMap.Load(Pair, new Box2i(-1, -1, 2, 2));
        await Server.WaitAssertion(() =>
        {
            var languages = SEntMan.System<CFLanguageSystem>();
            var boar = SEntMan.SpawnEntity("CFMobBoar", map.Local(0, 0));
            var skeleton = SEntMan.SpawnEntity("CFMobSkeletonguardMelee", map.Local(0, 0));
            var kobold = SEntMan.SpawnEntity("CFKoboldWarrior", map.Local(0, 0));

            Assert.Multiple(() =>
            {
                Assert.That(languages.TryGetSpoken(boar, out var growl) && growl.ID == "Bestial");
                Assert.That(languages.TryGetSpoken(skeleton, out var moan) && moan.ID == "Mindless");
                Assert.That(languages.Understands(kobold, "Draconic"));
                Assert.That(languages.Understands(kobold, "Common"), Is.False, "Talk to it in Draconic or Sakvroth.");
                Assert.That(languages.Understands(boar, "Common"), Is.False);
                Assert.That(CFLanguageRules.Pickable(Server.ProtoMan, null).Any(l => l.ID is "Bestial" or "Mindless"), Is.False);
            });
        });
    }

    [Test]
    public async Task ItIsLearnedAtLevelFive()
    {
        var map = await CFDevMap.Load(Pair, new Box2i(-1, -1, 2, 2));
        await Server.WaitAssertion(() =>
        {
            var hero = SEntMan.SpawnEntity("MobHuman", map.Local(0, 0));
            var build = new CFCharacterBuildData();
            build.Languages.AddRange(Picks("Goblin", "Dwarven"));
            build.AbilityBoostsLevel5.AddRange(new[] { CFAbility.Intelligence, CFAbility.Strength, CFAbility.Dexterity, CFAbility.Wisdom });
            build.AncestryFreeBoosts.Add(CFAbility.Intelligence);
            SEntMan.System<CFCharacterSpawnSystem>().ApplyBuild(hero, HumanoidCharacterProfile.DefaultWithSpecies().WithCFBuild(build));

            var known = SEntMan.GetComponent<CFLanguagesComponent>(hero).Known;
            var xp = SEntMan.GetComponent<CFExperienceComponent>(hero);
            Assume.That(SEntMan.GetComponent<CFAbilityScoreComponent>(hero).GetModifier(CFAbility.Intelligence), Is.EqualTo(1));
            Assert.That(known, Does.Not.Contain(new ProtoId<CFLanguagePrototype>("Dwarven")));
            Assert.That(xp.LanguageLevel5, Is.EqualTo("Dwarven"));

            SEntMan.System<CFExperienceSystem>().AddXpAndLevelUp(hero, 4 * CFExperienceComponent.XpPerLevel);
            Assert.That(known, Does.Contain(new ProtoId<CFLanguagePrototype>("Dwarven")));
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

    /// <summary>
    /// A stock kobold body swaps every line for "Yip" or "Grrar". A player in a kobold warrior has to read their own
    /// words, and a listener without Draconic still must not.
    /// </summary>
    [TestCase(InGameICChatType.Speak)]
    [TestCase(InGameICChatType.Whisper)]
    public async Task AKoboldUnderstandsItself(InGameICChatType type)
    {
        const string line = "Hello adventurer";
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid kobold = default;
        EntityUid human = default;

        await Server.WaitPost(() =>
        {
            kobold = SEntMan.SpawnEntity("CFKoboldWarrior", _map.Local(0, 0));
            human = SEntMan.SpawnEntity("MobHuman", _map.Local(1, 0));
            SEntMan.AddComponent<CFLanguagesComponent>(human).Known = Picks("Common");
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, kobold);
        });

        await Pair.RunTicksSync(10);
        Assert.That(SEntMan.HasComponent<ReplacementAccentComponent>(kobold), Is.False, "Its language replaces the noises.");

        var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var before = chat.History.Count;
        await Server.WaitPost(() =>
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(kobold, line, type, false, ignoreActionBlocker: true));
        await Pair.RunTicksSync(10);

        Assert.That(chat.History, Has.Count.GreaterThan(before), "The kobold heard itself.");
        var own = chat.History[^1].Msg;
        Assert.Multiple(() =>
        {
            Assert.That(own.Message, Does.Contain("Hello"));
            Assert.That(own.WrappedMessage, Does.Contain("adventurer"));
            Assert.That(own.WrappedMessage, Does.Contain("Draconic"), "It says which language it is in.");
        });

        await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(Pair.Player!, human));
        await Pair.RunTicksSync(10);
        before = chat.History.Count;
        await Server.WaitPost(() =>
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(kobold, line, type, false, ignoreActionBlocker: true));
        await Pair.RunTicksSync(10);

        Assert.That(chat.History, Has.Count.GreaterThan(before), "The human heard something.");
        var foreign = chat.History[^1].Msg;
        Assert.Multiple(() =>
        {
            Assert.That(foreign.Message, Does.Not.Contain("Hello"));
            Assert.That(foreign.WrappedMessage, Does.Not.Contain("adventurer"));
        });
    }

    /// <summary>The language menu's choice reaches the server, which only takes a language the body knows.</summary>
    [Test]
    public async Task TheLanguageMenuChoosesWhatYouSpeak()
    {
        _map = await CFDevMap.Load(Pair, Footprint);
        EntityUid kobold = default;
        await Server.WaitPost(() =>
        {
            kobold = SEntMan.SpawnEntity("CFKoboldWarrior", _map.Local(0, 0));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, kobold);
        });
        await Pair.RunTicksSync(10);

        var menu = CEntMan.System<CFLanguageMenuSystem>();
        var changes = 0;
        void Count() => changes++;
        menu.LanguagesChanged += Count;

        Assert.That(menu.LocalLanguages, Is.Not.Null, "The client knows what its body speaks.");
        Assert.That(CFLanguageSystem.Spoken(menu.LocalLanguages!).Id, Is.EqualTo("Draconic"));

        await Client.WaitPost(() => menu.RequestSpeak("Sakvroth"));
        await Pair.RunTicksSync(10);
        Assert.Multiple(() =>
        {
            Assert.That(SEntMan.GetComponent<CFLanguagesComponent>(kobold).Speaking?.Id, Is.EqualTo("Sakvroth"));
            Assert.That(CFLanguageSystem.Spoken(menu.LocalLanguages!).Id, Is.EqualTo("Sakvroth"), "The client was told.");
            Assert.That(changes, Is.GreaterThan(0), "The menu was told to redraw.");
        });

        await Client.WaitPost(() => menu.RequestSpeak("Common"));
        await Pair.RunTicksSync(10);
        Assert.That(SEntMan.GetComponent<CFLanguagesComponent>(kobold).Speaking?.Id, Is.EqualTo("Sakvroth"), "It knows no Common.");

        await Client.WaitAssertion(() =>
        {
            var window = new CFLanguageMenuWindow();
            window.Show("Sakvroth", menu.LocalLanguages!.Known);
            var buttons = window.KnownList.Children
                .Select(panel => panel.GetChild(0).Children.OfType<Button>().Single())
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(buttons, Has.Count.EqualTo(2), "Draconic and Sakvroth.");
                Assert.That(buttons.Count(button => button.Disabled), Is.EqualTo(1), "Only the one being spoken.");
                Assert.That(window.CurrentLanguageLabel.Text, Does.Contain("Sakvroth"));
            });
        });

        menu.LanguagesChanged -= Count;
    }
}
