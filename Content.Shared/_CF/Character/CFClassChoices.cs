using System.Linq;
using Content.Shared.CF.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

/// <summary>
/// What counts as a valid subclass or deity pick. A build comes from a client, so everything that reads one goes through
/// here and not to the lists on <see cref="CFCharacterBuildData"/>.
/// </summary>
public static class CFClassChoices
{
    /// <summary>The picks that are real, belong to the build's class, and are the first in their slot.</summary>
    public static List<CFClassChoicePrototype> Selected(IPrototypeManager prototypes, CFCharacterBuildData build)
    {
        var result = new List<CFClassChoicePrototype>();
        if (build.Class is not { } classId)
            return result;

        foreach (var id in build.ClassChoices)
        {
            if (!prototypes.TryIndex(id, out CFClassChoicePrototype? choice) || choice.Class != classId)
                continue;

            if (!result.Exists(other => other.Slot == choice.Slot))
                result.Add(choice);
        }

        return result;
    }

    /// <summary>The build's deity, if it has one and its class follows one.</summary>
    public static CFDeityPrototype? SelectedDeity(IPrototypeManager prototypes, CFCharacterBuildData build)
    {
        if (build.Deity is not { } deityId
            || build.Class is not { } classId
            || !prototypes.TryIndex(classId, out CFClassPrototype? cls)
            || !cls.UsesDeity
            || !prototypes.TryIndex(deityId, out CFDeityPrototype? deity))
        {
            return null;
        }

        return deity;
    }

    /// <summary>Every slot a class has options in, in a stable order.</summary>
    public static List<LocId> Slots(IPrototypeManager prototypes, ProtoId<CFClassPrototype> classId)
    {
        return prototypes.EnumeratePrototypes<CFClassChoicePrototype>()
            .Where(choice => choice.Class == classId)
            .Select(choice => choice.Slot)
            .Distinct()
            .OrderBy(slot => slot.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static CFAbility KeyAbility(IPrototypeManager prototypes, CFCharacterBuildData build, CFClassPrototype cls)
    {
        foreach (var choice in Selected(prototypes, build))
        {
            if (choice.KeyAbility is { } ability)
                return ability;
        }

        return cls.KeyAbility;
    }

    /// <summary>Skills trained by the build's subclass picks and deity.</summary>
    public static List<string> TrainedSkills(IPrototypeManager prototypes, CFCharacterBuildData build)
    {
        var result = new List<string>();
        foreach (var choice in Selected(prototypes, build))
        {
            foreach (var skill in choice.TrainedSkills)
                result.Add(skill.Id);
        }

        if (SelectedDeity(prototypes, build) is { } deity)
        {
            foreach (var skill in deity.TrainedSkills)
                result.Add(skill.Id);
        }

        return result;
    }

    /// <summary>Puts an option in its slot, replacing the old one. A skill it now trains stops being a wasted pick.</summary>
    public static void Assign(IPrototypeManager prototypes, CFCharacterBuildData build, ProtoId<CFClassChoicePrototype> id)
    {
        if (!prototypes.TryIndex(id, out CFClassChoicePrototype? choice))
            return;

        build.ClassChoices.RemoveAll(existing =>
            prototypes.TryIndex(existing, out CFClassChoicePrototype? other) && other.Slot == choice.Slot);
        build.ClassChoices.Add(id);
        DropRepeatedPicks(prototypes, build);
    }

    public static void AssignDeity(IPrototypeManager prototypes, CFCharacterBuildData build, ProtoId<CFDeityPrototype> id)
    {
        build.Deity = id;
        DropRepeatedPicks(prototypes, build);
    }

    /// <summary>Removes picks that no longer exist or no longer fit the build's class.</summary>
    public static void Sanitize(IPrototypeManager prototypes, CFCharacterBuildData build)
    {
        build.ClassChoices = Selected(prototypes, build)
            .Select(choice => new ProtoId<CFClassChoicePrototype>(choice.ID))
            .ToList();

        if (SelectedDeity(prototypes, build) == null)
            build.Deity = null;
    }

    private static void DropRepeatedPicks(IPrototypeManager prototypes, CFCharacterBuildData build)
    {
        var trained = TrainedSkills(prototypes, build);
        build.ClassTrainedSkills.RemoveAll(skill => trained.Contains(skill.Id));
    }
}
