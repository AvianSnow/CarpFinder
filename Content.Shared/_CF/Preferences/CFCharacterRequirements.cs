using Content.Shared.CF.Character;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Preferences;

/// <summary>What a character must have before it can join a round. The builder and the server both ask here.</summary>
public static class CFCharacterRequirements
{
    /// <summary>What is still to choose, as locale ids. Empty when the character is complete.</summary>
    public static List<LocId> Missing(IPrototypeManager prototypes, HumanoidCharacterProfile profile)
    {
        var missing = new List<LocId>();
        if (CFAttributeMath.FindAncestry(prototypes, profile.Species) == null)
            missing.Add("cf-character-missing-ancestry");

        if (profile.CFBuild.Background is not { } background || !prototypes.HasIndex(background))
            missing.Add("cf-character-missing-background");

        if (profile.CFBuild.Class is not { } chosenClass || !prototypes.HasIndex(chosenClass))
            missing.Add("cf-character-missing-class");

        if (ChosenJob(prototypes, profile) == null)
            missing.Add("cf-character-missing-job");

        return missing;
    }

    /// <summary>
    /// The player's favourite of the jobs the builder offers. A new character is set to stock's overflow job, which the
    /// builder never lists, so that one does not count as a choice.
    /// </summary>
    public static ProtoId<JobPrototype>? ChosenJob(IPrototypeManager prototypes, HumanoidCharacterProfile profile)
    {
        ProtoId<JobPrototype>? best = null;
        var bestPriority = JobPriority.Never;
        foreach (var group in prototypes.EnumeratePrototypes<CFJobGroupPrototype>())
        {
            foreach (var job in group.Roles)
            {
                var priority = profile.JobPriorities.GetValueOrDefault(job, JobPriority.Never);
                if (priority <= bestPriority)
                    continue;

                best = job;
                bestPriority = priority;
            }
        }

        return best;
    }
}
