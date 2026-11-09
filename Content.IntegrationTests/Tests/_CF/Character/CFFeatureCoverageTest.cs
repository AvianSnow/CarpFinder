using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.CF.Character;
using Content.Shared.CF.Character.Grants;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Robust.Shared.ContentPack;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.CF.Character;

/// <summary>Every character option either does something or says it does not yet, and none of its data is mistyped.</summary>
[TestFixture]
[TestOf(typeof(CFClassPrototype))]
public sealed class CFFeatureCoverageTest : GameTest
{
    private const string Placeholder = "implemented later";

    /// <summary><see cref="Content.Shared.CF.Companions.CFPetMath"/> looks for this feat by its id.</summary>
    private const string AnimalCompanion = "CFAnimalCompanion";

    public override PoolSettings PoolSettings => new() { Connected = false };

    private static readonly Dictionary<string, Type> Kinds = new()
    {
        ["cfAncestry"] = typeof(CFAncestryPrototype),
        ["cfBackground"] = typeof(CFBackgroundPrototype),
        ["cfClass"] = typeof(CFClassPrototype),
        ["cfClassChoice"] = typeof(CFClassChoicePrototype),
        ["cfFeat"] = typeof(CFFeatPrototype),
        ["cfHeritage"] = typeof(CFHeritagePrototype),
    };

    private IEnumerable<(string Source, CFGrant Grant)> AllGrants()
    {
        var prototypes = Server.ProtoMan;
        var lists = new List<(string Source, IEnumerable<CFGrant> Grants)>();
        lists.AddRange(prototypes.EnumeratePrototypes<CFFeatPrototype>().Select(p => (p.ID, (IEnumerable<CFGrant>) p.Grants)));
        lists.AddRange(prototypes.EnumeratePrototypes<CFHeritagePrototype>().Select(p => (p.ID, (IEnumerable<CFGrant>) p.Grants)));
        lists.AddRange(prototypes.EnumeratePrototypes<CFAncestryPrototype>().Select(p => (p.ID, (IEnumerable<CFGrant>) p.Grants)));
        lists.AddRange(prototypes.EnumeratePrototypes<CFBackgroundPrototype>().Select(p => (p.ID, (IEnumerable<CFGrant>) p.Grants)));
        foreach (var choice in prototypes.EnumeratePrototypes<CFClassChoicePrototype>())
        {
            lists.Add((choice.ID, choice.Grants));
            lists.AddRange(choice.Features.Select(f => ($"{choice.ID}/{f.Name}", (IEnumerable<CFGrant>) f.Grants)));
        }

        foreach (var cls in prototypes.EnumeratePrototypes<CFClassPrototype>())
        {
            lists.AddRange(cls.Features.Select(f => ($"{cls.ID}/{f.Name}", (IEnumerable<CFGrant>) f.Grants)));
        }

        return lists.SelectMany(list => list.Grants.Select(grant => (list.Source, grant)));
    }

    private HashSet<string> KnownProficiencies()
    {
        var known = new HashSet<string>
        {
            "Fortitude",
            "Reflex",
            "Will",
            "Perception",
            CFDefense.ClassDcProficiency,
            CFDefense.SpellProficiency,
        };
        known.UnionWith(Server.ProtoMan.EnumeratePrototypes<CFAbilitySkillPrototype>().Select(skill => skill.ID));
        known.UnionWith(Enum.GetNames<CFWeaponGroup>());
        known.UnionWith(Enum.GetValues<CFWeaponCategory>().Select(category => category.Proficiency()));
        foreach (var cls in Server.ProtoMan.EnumeratePrototypes<CFClassPrototype>())
        {
            foreach (var list in new[] { cls.Proficiencies.Attacks, cls.Proficiencies.Defenses })
            {
                known.UnionWith(list.Trained);
                known.UnionWith(list.Expert);
                known.UnionWith(list.Master);
                known.UnionWith(list.Legendary);
            }
        }

        return known;
    }

    [Test]
    public async Task EveryProficiencyAGrantRaisesIsOneTheRollsRead()
    {
        var unknown = new List<string>();
        await Server.WaitPost(() =>
        {
            var known = KnownProficiencies();
            foreach (var (source, grant) in AllGrants())
            {
                if (grant is not CFGrantProficiency proficiency)
                    continue;

                unknown.AddRange(proficiency.Proficiencies.Where(name => !known.Contains(name)).Select(name => $"{source}: {name}"));
            }
        });

        Assert.That(unknown, Is.Empty);
    }

    /// <summary>A prototype that cannot be read is dropped with one line in the log, so look for each one in the files.</summary>
    [Test]
    public async Task EveryCharacterOptionInTheFilesWasLoaded()
    {
        var missing = new List<string>();
        var found = 0;
        await Server.WaitPost(() =>
        {
            var resources = Server.ResolveDependency<IResourceManager>();
            foreach (var path in resources.ContentFindFiles(new ResPath("/Prototypes/_CF/Character")))
            {
                if (path.Extension != "yml")
                    continue;

                using var reader = resources.ContentFileReadText(path);
                string? kind = null;
                while (reader.ReadLine() is { } line)
                {
                    if (line.StartsWith("- type: ", StringComparison.Ordinal))
                    {
                        kind = line["- type: ".Length..].Trim();
                        continue;
                    }

                    if (kind == null || !line.StartsWith("  id:", StringComparison.Ordinal))
                        continue;

                    var id = line["  id:".Length..].Trim();
                    if (Kinds.TryGetValue(kind, out var type))
                    {
                        found++;
                        if (!Server.ProtoMan.TryIndex(type, id, out _))
                            missing.Add($"{kind} {id} in {path}");
                    }

                    kind = null;
                }
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.GreaterThan(1000), "The files were not read.");
            Assert.That(missing, Is.Empty);
        });
    }

    [Test]
    public async Task NoClassHasAPlaceholderLeft()
    {
        var features = new List<string>();
        var feats = new List<string>();
        var silent = new List<string>();
        await Server.WaitPost(() =>
        {
            foreach (var cls in Server.ProtoMan.EnumeratePrototypes<CFClassPrototype>())
            {
                features.AddRange(cls.Features.Where(f => f.Description.Contains(Placeholder)).Select(f => $"{cls.ID}/{f.Name}"));
            }

            foreach (var feat in Server.ProtoMan.EnumeratePrototypes<CFFeatPrototype>())
            {
                if (feat.Class == null)
                    continue;

                var text = Loc.GetString(feat.Description);
                if (text.Contains(Placeholder))
                    feats.Add(feat.ID);

                var hasEffect = feat.Grants.Count > 0
                                || feat.Choices.Count > 0
                                || feat.TrainedSkills.Count > 0
                                || feat.Lores.Count > 0
                                || feat.SpeedBonus != 0
                                || feat.NaturalDie != 0
                                || feat.ID == AnimalCompanion;
                if (!hasEffect && !text.Contains("TBA") && !text.Contains("ot applied in play"))
                    silent.Add(feat.ID);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(features, Is.Empty, "Class features still waiting for their text.");
            Assert.That(feats, Is.Empty, "Class feats still waiting for their text.");
            Assert.That(silent, Is.Empty, "A class feat with no effect has to say TBA.");
        });
    }

    [Test]
    public async Task OnlyAFeatureWithNothingWorkingIsMarked()
    {
        var unfinished = false;
        var granted = false;
        var signpost = false;
        await Server.WaitPost(() =>
        {
            var fighter = Server.ProtoMan.Index<CFClassPrototype>("Fighter");
            var witch = Server.ProtoMan.Index<CFClassPrototype>("Witch");
            granted = fighter.Features.First(f => f.Name == "Fighter Weapon Mastery").Unfinished;
            signpost = witch.Features.First(f => f.Name == "Patron").Unfinished;
            unfinished = Server.ProtoMan.EnumeratePrototypes<CFClassPrototype>()
                .SelectMany(cls => cls.Features)
                .Any(f => f.Unfinished);
        });

        Assert.Multiple(() =>
        {
            Assert.That(granted, Is.False, "It has grants, and only part of its text says TBA.");
            Assert.That(signpost, Is.False, "It points at a builder button and has nothing left to do.");
            Assert.That(unfinished, Is.True, "Nothing would ever be marked.");
        });
    }
}
