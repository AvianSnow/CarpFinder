using System.Linq;
using Content.Shared.CF.Combat;
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

    /// <summary>What a class feature's picks are stored under.</summary>
    public static string FeatureSource(CFClassPrototype cls, CFClassFeature feature)
    {
        return $"class:{cls.ID}:{feature.Name}";
    }

    /// <summary>The ids a choice can be answered with.</summary>
    public static List<string> Allowed(IPrototypeManager prototypes, CFOptionChoice choice)
    {
        if (choice.Kind == CFOptionChoiceKind.WeaponGroup)
            return Enum.GetNames<CFWeaponGroup>().ToList();

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

    /// <summary>The build's answers that fit something it has. The same answer cannot fill two slots of one option.</summary>
    public static List<CFOptionPick> Valid(IPrototypeManager prototypes, CFCharacterBuildData build, string? ancestryId)
    {
        var result = new List<CFOptionPick>();
        foreach (var slot in Slots(prototypes, build, ancestryId))
        {
            var pick = build.OptionPicks.FirstOrDefault(p => p.Source == slot.Source && p.Slot == slot.Slot);
            if (pick == null
                || !Allowed(prototypes, slot.Choice).Contains(pick.Value)
                || result.Exists(other => other.Source == pick.Source && other.Value == pick.Value))
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
        var picks = Valid(prototypes, build, ancestryId);
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
