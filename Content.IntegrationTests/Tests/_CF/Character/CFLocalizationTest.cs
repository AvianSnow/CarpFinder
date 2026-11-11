using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.CF.Character;
using Content.Shared.CF.Companions;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>
/// A <c>LocId</c> holding plain English still shows up in game, so nothing looks wrong until someone reads the logs. These
/// asserts are on the test thread, so a failure keeps its list.
/// </summary>
[TestFixture]
public sealed class CFLocalizationTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task EveryCharacterOptionHasRealText()
    {
        var missing = new List<string>();
        await Server.WaitPost(() =>
        {
            var loc = Server.ResolveDependency<ILocalizationManager>();

            void Check(string kind, string id, string key)
            {
                if (!loc.HasString(key))
                    missing.Add($"{kind} {id}: {key}");
            }

            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>())
            {
                Check("feat", feat.ID, feat.Name);
                Check("feat", feat.ID, feat.Description);
            }

            foreach (var background in Server.ProtoMan.EnumeratePrototypes<CFBackgroundPrototype>())
            {
                Check("background", background.ID, background.Name);
                Check("background", background.ID, background.Description);
            }

            foreach (var heritage in Server.ProtoMan.EnumeratePrototypes<CFHeritagePrototype>())
            {
                Check("heritage", heritage.ID, heritage.Name);
                Check("heritage", heritage.ID, heritage.Description);
            }

            foreach (var choice in Server.ProtoMan.EnumeratePrototypes<CFClassChoicePrototype>())
            {
                Check("class choice", choice.ID, choice.Slot);
                Check("class choice", choice.ID, choice.Name);
                Check("class choice", choice.ID, choice.Description);
            }

            foreach (var deity in Server.ProtoMan.EnumeratePrototypes<CFDeityPrototype>())
            {
                Check("deity", deity.ID, deity.Name);
                Check("deity", deity.ID, deity.Description);
            }
        });

        Assert.That(missing, Is.Empty);
    }

    /// <summary>Text with a stray brace loses a message, or a whole file, with no error at the place it is used.</summary>
    [Test]
    public async Task FeatTextIsWhatTheFileSays()
    {
        string name = null;
        string description = null;
        await Server.WaitPost(() =>
        {
            var loc = Server.ResolveDependency<ILocalizationManager>();
            var feat = Server.ProtoMan.Index(new ProtoId<CFFeatPrototype>("CFQuickBomber"));
            name = loc.GetString(feat.Name);
            description = loc.GetString(feat.Description);
        });

        Assert.That(name, Is.EqualTo("Quick Bomber"));
        Assert.That(description, Does.Contain("Alchemical bombs"));
    }

    /// <summary>Pets are matched on the feat's id. Its name is only a key, and searching that would quietly find nothing.</summary>
    [Test]
    public async Task ClassesStillOfferTheirPets()
    {
        var companion = false;
        var familiar = false;
        var none = true;
        await Server.WaitPost(() =>
        {
            var prototypes = Server.ProtoMan;
            var feats = prototypes.EnumeratePrototypes<CFFeatPrototype>().ToList();
            var companionFeat = feats.First(feat => feat.ID.Contains("AnimalCompanion") && feat.Class != null);
            var familiarFeat = feats.First(feat => feat.ID.Contains("Familiar") && !feat.ID.Contains("Familiarity") && feat.Class != null);
            companion = CFPetMath.ClassHasCompanion(prototypes, companionFeat.Class);
            familiar = CFPetMath.ClassHasFamiliar(prototypes, familiarFeat.Class);
            none = CFPetMath.ClassHasCompanion(prototypes, "NoSuchClass");
        });

        Assert.Multiple(() =>
        {
            Assert.That(companion);
            Assert.That(familiar);
            Assert.That(none, Is.False);
        });
    }
}
