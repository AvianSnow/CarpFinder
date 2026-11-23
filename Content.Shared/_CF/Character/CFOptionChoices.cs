using System.Linq;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

/// <summary>One thing the player has to pick for an option in the build.</summary>
public readonly record struct CFOptionSlot(string Source, LocId SourceName, int Slot, CFOptionChoice Choice);

/// <summary>
/// What a build has to choose for its feats, heritage and subclass, and whether its answers are valid. A build comes from a
/// client, so everything that reads one goes through here and not to <see cref="CFCharacterBuildData.OptionPicks"/>.
/// </summary>
public static class CFOptionChoices
{
    /// <summary>Marks an answer as a signature spell, because a plain spell id means a cantrip to learn.</summary>
    public const string SignaturePrefix = "signature:";

    /// <summary>Everything the build has to pick for what it has by <paramref name="level"/>.</summary>
    public static List<CFOptionSlot> Slots(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        int level = CFExperienceComponent.MaxLevel)
    {
        var result = new List<CFOptionSlot>();
        if (build.Heritage is { } heritageId
            && prototypes.TryIndex(heritageId, out CFHeritagePrototype? heritage)
            && heritage.Fits(ancestryId))
        {
            AddSlots(result, heritage.ID, heritage.Name, heritage.Choices);
        }

        foreach (var choice in CFClassChoices.Selected(prototypes, build))
        {
            AddSlots(result, choice.ID, choice.Name, choice.Choices);
        }

        if (build.Class is { } classId && prototypes.TryIndex(classId, out CFClassPrototype? cls))
        {
            foreach (var feature in cls.Features)
            {
                if (feature.Level <= level && feature.Choices.Count > 0 && feature.ChoiceTitle is { } title)
                    AddSlots(result, FeatureSource(cls, feature), title, feature.Choices);
            }
        }

        foreach (var feat in CFSkills.SelectedFeats(prototypes, build, ancestryId, build.Class?.Id, level))
        {
            AddSlots(result, feat.ID, feat.Name, feat.Choices);
        }

        return result;
    }

    /// <summary>The picks that can be changed each day, for what the build has by <paramref name="level"/>.</summary>
    public static List<CFOptionSlot> DailySlots(IPrototypeManager prototypes, CFCharacterBuildData build, string? ancestryId, int level)
    {
        var slots = Slots(prototypes, build, ancestryId, level);
        slots.RemoveAll(slot => !slot.Choice.Daily);
        return slots;
    }

    /// <summary>
    /// A plan for the next long rest: every pick has to be for a daily slot the build has, and one of that slot's answers.
    /// It comes from a client, so the server checks it with this.
    /// </summary>
    public static bool IsValidDailyPlan(
        IPrototypeManager prototypes,
        CFCharacterBuildData build,
        string? ancestryId,
        int level,
        IReadOnlyList<CFOptionPick> plan)
    {
        var slots = DailySlots(prototypes, build, ancestryId, level);
        var seen = new HashSet<(string, int)>();
        foreach (var pick in plan)
        {
            var slot = slots.Find(s => s.Source == pick.Source && s.Slot == pick.Slot);
            if (slot.Choice == null
                || !seen.Add((pick.Source, pick.Slot))
                || !Allowed(prototypes, slot.Choice, build, ancestryId).Contains(pick.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What a class feature's picks are stored under.</summary>
    public static string FeatureSource(CFClassPrototype cls, CFClassFeature feature)
    {
        return $"class:{cls.ID}:{feature.Name}";
    }

    /// <summary>
    /// The ids a choice can be answered with. A signature spell needs <paramref name="build"/>, to see what it knows, and a feat
    /// needs it to see what it qualifies for, with <paramref name="ancestryId"/>.
    /// </summary>
    public static List<string> Allowed(IPrototypeManager prototypes, CFOptionChoice choice, CFCharacterBuildData? build = null, string? ancestryId = null)
    {
        if (choice.Kind == CFOptionChoiceKind.Feat)
        {
            var feats = new List<string>();
            if (build == null)
                return feats;

            foreach (var feat in prototypes.EnumeratePrototypes<CFFeatPrototype>())
            {
                if (FeatFits(choice, feat, build.Class?.Id, ancestryId)
                    && CFSkills.Qualifies(prototypes, build, ancestryId, feat, choice.FeatLevel)
                    && (choice.JoinsSubclass
                        ? feat.RequiresChoice.Count > 0 && !CFClassChoices.HasOwn(prototypes, build, feat)
                        : choice.FeatSlot != CFLevelSlot.ClassFeat || CFClassChoices.MeetsRequirement(prototypes, build, feat)))
                {
                    feats.Add(feat.ID);
                }
            }

            return feats;
        }

        if (choice.Kind == CFOptionChoiceKind.SignatureSpell)
        {
            var known = new List<string>();
            if (build == null)
                return known;

            foreach (var pick in build.Spells)
            {
                var id = SignaturePrefix + pick.Spell.Id;
                if (pick.HasSpell
                    && prototypes.TryIndex(pick.Spell, out var spell)
                    && (choice.SpellRank == 0 ? spell.Rank > 0 : spell.Rank == choice.SpellRank)
                    && !known.Contains(id))
                {
                    known.Add(id);
                }
            }

            return known;
        }

        if (choice.Kind == CFOptionChoiceKind.WeaponGroup)
            return Enum.GetNames<CFWeaponGroup>().ToList();

        if (choice.Kind == CFOptionChoiceKind.Domain)
            return Domains(prototypes, choice, build);

        if (choice.Kind == CFOptionChoiceKind.Terrain)
            return Enum.GetNames<Content.Shared.CF.Terrain.CFFavoredTerrain>().ToList();

        if (choice.Kind == CFOptionChoiceKind.DamageType)
            return choice.DamageTypes.Select(type => type.Id).ToList();

        if (choice.Kind == CFOptionChoiceKind.Language)
            return CFLanguageRules.Pickable(prototypes, null).Select(language => language.ID).ToList();

        if (choice.Kind == CFOptionChoiceKind.KeyAttribute)
            return choice.Abilities.Select(ability => ability.ToString()).ToList();

        if (choice.Kind == CFOptionChoiceKind.SaveOrPerception)
            return new List<string> { "Fortitude", "Reflex", "Will", "Perception" };

        if (choice.Kind == CFOptionChoiceKind.Skill)
        {
            return choice.Skills.Count > 0
                ? choice.Skills.Select(skill => skill.Id).ToList()
                : CFSkills.Column1.Concat(CFSkills.Column2).Concat(CFSkills.Column3).ToList();
        }

        return prototypes.EnumeratePrototypes<CFSpellPrototype>()
            .Where(spell => spell.Rank == 0
                            && prototypes.HasIndex<EntityPrototype>("CF" + spell.ID)
                            && (choice.Traditions.Count == 0 || choice.Traditions.Any(spell.HasTradition)))
            .Select(spell => spell.ID)
            .ToList();
    }

    /// <summary>Whether a feat is of the kind and level a <see cref="CFOptionChoiceKind.Feat"/> choice offers. Prerequisites are not checked here.</summary>
    /// <summary>
    /// The domains a domain feat can pick: every domain for Splinter Faith itself, the four Splinter Faith picked when the
    /// build has it, or else the deity's.
    /// </summary>
    private static List<string> Domains(IPrototypeManager prototypes, CFOptionChoice choice, CFCharacterBuildData? build)
    {
        if (choice.AnyDomain)
            return prototypes.EnumeratePrototypes<CFDomainPrototype>().Select(domain => domain.ID).ToList();

        if (build == null)
            return new List<string>();

        var splinter = new List<string>();
        foreach (var pick in build.OptionPicks)
        {
            if (pick.Source.StartsWith("CFSplinterFaith", StringComparison.Ordinal) && prototypes.HasIndex<CFDomainPrototype>(pick.Value))
                splinter.Add(pick.Value);
        }

        if (splinter.Count > 0)
            return splinter;

        return CFClassChoices.SelectedDeity(prototypes, build)?.Domains.Select(domain => domain.Id).ToList() ?? new List<string>();
    }

    public static bool FeatFits(CFOptionChoice choice, CFFeatPrototype feat, string? classId, string? ancestryId)
    {
        if (choice.FeatTrait is { } trait && !feat.Traits.Contains(trait))
            return false;

        if (choice.Feats.Count > 0 && !choice.Feats.Contains(feat.ID))
            return false;

        return choice.FeatSlot switch
        {
            CFLevelSlot.ClassFeat => feat.FitsClass(classId, choice.FeatLevel),
            CFLevelSlot.AncestryFeat => feat.Fits(ancestryId, choice.FeatLevel),
            CFLevelSlot.SkillFeat or CFLevelSlot.BonusSkillFeat => feat.FitsSkillSlot(choice.FeatLevel),
            _ => feat.FitsGeneralSlot(choice.FeatLevel),
        };
    }

    /// <summary>The build's answers that fit something it has. The same answer cannot fill two slots of one option.</summary>
    /// <param name="checkFeats">
    /// Whether a feat pick has to qualify, which reads the build's skill ranks. Reading those ranks comes back here for the skills
    /// the picks train, so that path passes false and takes a feat pick of the right kind as it is.
    /// </param>
    public static List<CFOptionPick> Valid(IPrototypeManager prototypes, CFCharacterBuildData build, string? ancestryId, bool checkFeats = true)
    {
        var result = new List<CFOptionPick>();
        foreach (var slot in Slots(prototypes, build, ancestryId))
        {
            var pick = build.OptionPicks.FirstOrDefault(p => p.Source == slot.Source && p.Slot == slot.Slot);
            if (pick == null || result.Exists(other => other.Source == pick.Source && other.Value == pick.Value))
                continue;

            if (slot.Choice.Kind == CFOptionChoiceKind.Feat && !checkFeats)
            {
                if (!prototypes.TryIndex<CFFeatPrototype>(pick.Value, out var feat) || !FeatFits(slot.Choice, feat, build.Class?.Id, ancestryId))
                    continue;
            }
            else if (!Allowed(prototypes, slot.Choice, build, ancestryId).Contains(pick.Value))
            {
                continue;
            }

            result.Add(pick.Copy());
        }

        return result;
    }

    /// <summary>Skills the build's answers train by <paramref name="level"/>.</summary>
    public static List<string> TrainedSkills(IPrototypeManager prototypes, CFCharacterBuildData build, string? ancestryId, int level)
    {
        var result = new List<string>();
        var picks = Valid(prototypes, build, ancestryId, checkFeats: false);
        foreach (var slot in Slots(prototypes, build, ancestryId, level))
        {
            if (slot.Choice.Kind != CFOptionChoiceKind.Skill)
                continue;

            var pick = picks.FirstOrDefault(p => p.Source == slot.Source && p.Slot == slot.Slot);
            if (pick != null)
                result.Add(pick.Value);
        }

        return result;
    }

    private static void AddSlots(List<CFOptionSlot> result, string source, LocId name, List<CFOptionChoice> choices)
    {
        var index = 0;
        foreach (var choice in choices)
        {
            for (var i = 0; i < choice.Count; i++)
            {
                result.Add(new CFOptionSlot(source, name, index++, choice));
            }
        }
    }
}

/// <summary>Sent by the character sheet's Daily tab: what each daily pick should become at the next long rest. The server checks it.</summary>
[Serializable, Robust.Shared.Serialization.NetSerializable]
public sealed class CFPlanDailyChoicesEvent : EntityEventArgs
{
    public List<CFOptionPick> Picks = new();
}
