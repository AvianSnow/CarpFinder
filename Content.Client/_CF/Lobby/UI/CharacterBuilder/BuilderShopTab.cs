using Content.Client.CF.Character;
using Content.Shared.CF.Preferences;
using Content.Shared.CF.Shop;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.CF.Lobby.UI.CharacterBuilder;

/// <summary>
/// Character-creation shop, on the same view as a merchant's. Buying adds a loadout the character spawns with, and
/// further copies of it go in their bag. Selling is on the Weapons, Defense and Gear tabs.
/// </summary>
public sealed class BuilderShopTab : BoxContainer
{
    private readonly CFShopView _shop = new(scanItems: false);
    private readonly Label _notice;

    /// <summary>Which loadout group each listing belongs to. Listings are keyed by loadout id.</summary>
    private readonly Dictionary<string, string> _groupOf = new();

    private HumanoidCharacterProfile? _profile;
    private JobPrototype? _jobOverride;
    private ICommonSession? _session;
    private IPrototypeManager? _prototypes;
    private Action<HumanoidCharacterProfile>? _onChanged;

    public BuilderShopTab()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        _notice = new Label { Visible = false, Margin = new Thickness(10) };
        AddChild(_notice);

        _shop.PurseSource = Purse;
        _shop.Checkout = Buy;
        _shop.HideCancel();
        AddChild(_shop);
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
        _ = collection;
        _ = getProfile;
        _profile = profile;
        _jobOverride = jobOverride;
        _session = session;
        _prototypes = prototypes;
        _onChanged = onChanged;
        Paint();
    }

    public void Apply(HumanoidCharacterProfile profile)
    {
        if (_prototypes == null || _onChanged == null)
            return;

        _profile = profile;
        Paint();
    }

    private int Purse()
    {
        if (_profile == null || _prototypes == null)
            return CFCoins.StartingCopper(1);

        var job = _jobOverride ?? CFBuilderRole.PreferredJob(_profile, _prototypes);
        return CFCoins.Remaining(_profile, job.ID, _prototypes);
    }

    private void Paint()
    {
        _groupOf.Clear();
        var listings = new List<CFShopListing>();
        _notice.Visible = false;
        if (_profile != null && _prototypes != null)
        {
            if (CFBuilderRole.TryGet(_profile, _jobOverride, _session, _prototypes, out var role, out _))
            {
                foreach (var groupId in role.Groups)
                {
                    AddGroup(groupId, listings);
                }
            }
            else
            {
                _notice.Text = Loc.GetString("cf-character-builder-no-role-loadout");
                _notice.Visible = true;
            }
        }

        _shop.SetListings(listings);
    }

    private void AddGroup(ProtoId<LoadoutGroupPrototype> groupId, List<CFShopListing> listings)
    {
        if (!_prototypes!.Resolve(groupId, out var group) || group.Hidden)
            return;

        if (!string.Equals(group.EditorTab, LoadoutEditorTabs.Weapons, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(group.EditorTab, LoadoutEditorTabs.Defense, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(group.EditorTab, LoadoutEditorTabs.Gear, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var loadoutId in group.Loadouts)
        {
            if (!_prototypes.TryIndex(loadoutId, out var loadout)
                || CFCoins.LoadoutItem(loadout) is not { } item
                || !_prototypes.TryIndex<EntityPrototype>(item, out var proto)
                || CFCoins.EntityPrice(proto) <= 0)
            {
                continue;
            }

            _groupOf[loadout.ID] = group.ID;
            listings.Add(CFShopView.ToListing(proto, loadout.ID));
        }
    }

    private void Buy(List<CFShopPurchaseLine> lines)
    {
        if (_profile == null
            || _prototypes == null
            || _onChanged == null
            || !CFBuilderRole.TryGet(_profile, _jobOverride, _session, _prototypes, out _, out var roleLoadout))
        {
            return;
        }

        var build = _profile.CFBuild.Clone();
        foreach (var line in lines)
        {
            if (!_groupOf.TryGetValue(line.Prototype, out var group))
                continue;

            var copies = Math.Max(1, line.Quantity);
            if (!Owns(roleLoadout, group, line.Prototype))
            {
                if (!roleLoadout.AddLoadout(group, line.Prototype, _prototypes))
                    continue;

                copies--;
            }

            if (copies > 0)
                build.LoadoutCopies[line.Prototype] = Math.Min(CFCoins.CopiesOf(build, line.Prototype) + copies, CFCoins.MaxCopies);
        }

        _onChanged(_profile.WithLoadout(roleLoadout).WithCFBuild(build));
    }

    private static bool Owns(RoleLoadout role, string group, string loadout)
    {
        if (!role.SelectedLoadouts.TryGetValue(group, out var picks))
            return false;

        foreach (var pick in picks)
        {
            if (pick.Prototype == loadout)
                return true;
        }

        return false;
    }
}
