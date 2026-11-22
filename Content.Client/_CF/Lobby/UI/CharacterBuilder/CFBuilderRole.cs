using Content.Shared.CF.Preferences;
using Content.Shared.Clothing;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.CF.Lobby.UI.CharacterBuilder;

/// <summary>The role loadout the character builder is editing for the preferred or overridden job.</summary>
public static class CFBuilderRole
{
    public static bool TryGet(
        HumanoidCharacterProfile profile,
        JobPrototype? jobOverride,
        ICommonSession? session,
        IPrototypeManager prototypes,
        out RoleLoadoutPrototype roleProto,
        out RoleLoadout roleLoadout)
    {
        roleProto = default!;
        roleLoadout = default!;
        if (session == null)
            return false;

        var job = jobOverride ?? PreferredJob(profile, prototypes);
        var roleId = LoadoutSystem.GetJobPrototype(job.ID);
        if (!prototypes.TryIndex<RoleLoadoutPrototype>(roleId, out var resolved) || resolved == null)
            return false;

        roleProto = resolved;
        // A copy: the profile shares its loadouts with the saved one, so editing this in place changed both, the
        // editor saw nothing to save, and a bought item never reached the server.
        roleLoadout = profile.GetLoadoutOrDefault(
            roleId,
            session,
            profile.Species,
            IoCManager.Resolve<IEntityManager>(),
            prototypes).Clone();
        return true;
    }

    public static JobPrototype PreferredJob(HumanoidCharacterProfile profile, IPrototypeManager prototypes)
    {
        return prototypes.Index(CFCharacterRequirements.ChosenJob(prototypes, profile) ?? GameTicker.FallbackOverflowJob);
    }
}
