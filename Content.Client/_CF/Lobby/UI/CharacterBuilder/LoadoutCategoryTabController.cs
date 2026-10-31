using System.Linq;
using Content.Client.Lobby.UI.Loadouts;
using Content.Shared.Clothing;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.CF.Lobby.UI.CharacterBuilder;

/// <summary>
/// Fills a character-builder tab with the role loadout groups for one editor tab.
/// </summary>
public sealed class LoadoutCategoryTabController
{
    private readonly BoxContainer _groups;
    private readonly string _category;
    private readonly List<LoadoutGroupContainer> _containers = new();

    private RoleLoadout? _loadout;
    private ICommonSession? _session;
    private IDependencyCollection? _collection;

    public LoadoutCategoryTabController(BoxContainer groups, string category)
    {
        _groups = groups;
        _category = category;
    }

    public void Rebuild(
        HumanoidCharacterProfile? profile,
        JobPrototype? jobOverride,
        ICommonSession? session,
        IDependencyCollection? collection,
        IPrototypeManager prototypes,
        Func<HumanoidCharacterProfile?> getProfile,
        Action<HumanoidCharacterProfile> onChanged)
    {
        _groups.RemoveAllChildren();
        _containers.Clear();
        _loadout = null;
        _session = session;
        _collection = collection;

        if (profile == null || session == null || collection == null)
            return;

        if (!TryGetRole(profile, jobOverride, session, prototypes, out var roleProto, out var roleLoadout))
        {
            _groups.AddChild(new Label
            {
                Text = Loc.GetString("cf-character-builder-no-role-loadout"),
            });
            return;
        }

        _loadout = roleLoadout;

        var matched = false;
        foreach (var groupId in roleProto.Groups)
        {
            if (!prototypes.Resolve(groupId, out var group))
                continue;

            if (group.Hidden || !string.Equals(group.EditorTab, _category, StringComparison.OrdinalIgnoreCase))
                continue;

            matched = true;
            var container = new LoadoutGroupContainer(profile, roleLoadout, group, session, collection);
            _containers.Add(container);

            container.OnLoadoutPressed += proto =>
            {
                Commit(proto, add: true, group.ID, prototypes, getProfile, onChanged);
            };

            container.OnLoadoutUnpressed += proto =>
            {
                Commit(proto, add: false, group.ID, prototypes, getProfile, onChanged);
            };

            _groups.AddChild(new Label
            {
                Text = Loc.GetString(group.Name),
                StyleClasses = { "LabelHeading" },
                Margin = new Thickness(0, 8, 0, 4),
            });
            _groups.AddChild(container);
        }

        if (!matched)
        {
            _groups.AddChild(new Label
            {
                Text = Loc.GetString("cf-character-builder-no-loadouts"),
            });
        }
    }

    public void Apply(HumanoidCharacterProfile profile)
    {
        if (_loadout == null || _session == null || _collection == null)
            return;

        foreach (var container in _containers)
            container.RefreshLoadouts(profile, _loadout, _session, _collection);
    }

    private void Commit(
        ProtoId<LoadoutPrototype> proto,
        bool add,
        string groupId,
        IPrototypeManager prototypes,
        Func<HumanoidCharacterProfile?> getProfile,
        Action<HumanoidCharacterProfile> onChanged)
    {
        var current = getProfile();
        if (current == null || _loadout == null)
            return;

        if (add)
            _loadout.AddLoadout(groupId, proto, prototypes);
        else
            _loadout.RemoveLoadout(groupId, proto, prototypes);

        var updated = current.WithLoadout(_loadout);
        onChanged(updated);
        Apply(updated);
    }

    private static bool TryGetRole(
        HumanoidCharacterProfile profile,
        JobPrototype? jobOverride,
        ICommonSession session,
        IPrototypeManager prototypes,
        out RoleLoadoutPrototype roleProto,
        out RoleLoadout roleLoadout)
    {
        roleProto = default!;
        roleLoadout = default!;

        var job = jobOverride ?? GetPreferredJob(profile, prototypes);
        var roleId = LoadoutSystem.GetJobPrototype(job.ID);
        if (!prototypes.TryIndex<RoleLoadoutPrototype>(roleId, out var resolved) || resolved == null)
            return false;

        roleProto = resolved;
        roleLoadout = profile.GetLoadoutOrDefault(
            roleId,
            session,
            profile.Species,
            IoCManager.Resolve<IEntityManager>(),
            prototypes);
        return true;
    }

    private static JobPrototype GetPreferredJob(HumanoidCharacterProfile profile, IPrototypeManager prototypes)
    {
        var highPriorityJob = profile.JobPriorities.FirstOrDefault(p => p.Value == JobPriority.High).Key;
        var jobId = string.IsNullOrEmpty(highPriorityJob) ? GameTicker.FallbackOverflowJob : highPriorityJob;
        return prototypes.Index<JobPrototype>(jobId);
    }
}
