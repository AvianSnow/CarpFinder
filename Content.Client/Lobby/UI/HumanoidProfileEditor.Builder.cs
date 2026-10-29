using System.Linq;
using Content.Client.Lobby.UI.CharacterBuilder;
using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private const int DetailsTab = 0;
    private const int WeaponsTabIndex = 1;
    private const int DefenseTabIndex = 2;
    private const int GearTabIndex = 3;
    private const int SpellsTabIndex = 4;

    private enum BuilderSelection
    {
        Ancestry,
        Background,
        Class,
        Job,
    }

    private BuilderSelection _selection = BuilderSelection.Ancestry;
    private bool _pickerOpen;
    private string? _pendingId;
    private string? _backgroundId;
    private string? _classId;
    private string? _prdUrl;

    private const string AncestryPrd = "https://2e.aonprd.com/Ancestries.aspx";
    private const string BackgroundPrd = "https://2e.aonprd.com/Backgrounds.aspx";
    private const string ClassPrd = "https://2e.aonprd.com/Classes.aspx";

    private void SetupCharacterBuilder()
    {
        var group = new ButtonGroup();
        AncestryButton.Group = group;
        BackgroundButton.Group = group;
        ClassButton.Group = group;
        JobButton.Group = group;

        AncestryButton.OnPressed += _ => ShowAncestrySelection();
        BackgroundButton.OnPressed += _ => ShowBackgroundSelection();
        ClassButton.OnPressed += _ => ShowClassSelection();
        JobButton.OnPressed += _ => ShowJobSelection();
        JobCloseButton.OnPressed += _ => ClosePicker();
        JobAcceptButton.OnPressed += _ => ClosePicker();
        JobCancelButton.OnPressed += _ => ClosePicker();

        SelectionView.OnEntrySelected += id => _pendingId = id;
        SelectionView.OnAccept += AcceptPicker;
        SelectionView.OnClosed += ClosePicker;
        SelectionView.OnPrdPressed += OpenPrd;

        TabContainer.SetTabTitle(DetailsTab, Loc.GetString("cf-character-builder-details-tab"));
        TabContainer.SetTabTitle(WeaponsTabIndex, Loc.GetString("cf-character-builder-weapons-tab"));
        TabContainer.SetTabTitle(DefenseTabIndex, Loc.GetString("cf-character-builder-defense-tab"));
        TabContainer.SetTabTitle(GearTabIndex, Loc.GetString("cf-character-builder-gear-tab"));
        TabContainer.SetTabTitle(SpellsTabIndex, Loc.GetString("cf-character-builder-spells-tab"));

        UpdateBuilderSummary();
        Level2Toggle.OnPressed += _ => Level2Body.Visible = Level2Toggle.Pressed;
        Level3Toggle.OnPressed += _ => Level3Body.Visible = Level3Toggle.Pressed;
    }

    private void AcceptPicker()
    {
        var id = _pendingId;
        ClosePicker();
        if (id != null)
            OnBuilderOptionSelected(id);
    }

    private void ClosePicker()
    {
        _pickerOpen = false;
        _pendingId = null;
        SelectionView.Visible = false;
        JobPane.Visible = false;
        EditorPane.Visible = true;
        AncestryButton.Pressed = false;
        BackgroundButton.Pressed = false;
        ClassButton.Pressed = false;
        JobButton.Pressed = false;
    }

    private void OpenPicker()
    {
        EditorPane.Visible = false;
        JobPane.Visible = false;
        SelectionView.Visible = true;
        _pickerOpen = true;
    }

    private void ShowJobSelection()
    {
        _selection = BuilderSelection.Job;
        _pickerOpen = false;
        EditorPane.Visible = false;
        SelectionView.Visible = false;
        JobPane.Visible = true;
        RefreshJobs();
    }

    private void OpenPrd()
    {
        if (string.IsNullOrWhiteSpace(_prdUrl))
            return;

        IoCManager.Resolve<IUriOpener>().OpenUri(new Uri(_prdUrl));
    }

    private void OnBuilderOptionSelected(string id)
    {
        switch (_selection)
        {
            case BuilderSelection.Ancestry:
                SetSpecies(id);
                UpdateBuilderSummary();
                break;
            case BuilderSelection.Background:
                _backgroundId = id;
                UpdateBuilderSummary();
                break;
            case BuilderSelection.Class:
                _classId = id;
                UpdateBuilderSummary();
                break;
        }
    }

    private void ShowAncestrySelection()
    {
        _selection = BuilderSelection.Ancestry;
        _prdUrl = AncestryPrd;
        var selected = Profile?.Species.Id;
        _pendingId = selected;
        var covered = new HashSet<string>();
        var options = new List<Pf2eListEntry>();

        foreach (var ancestry in _prototypeManager.EnumeratePrototypes<CFAncestryPrototype>().OrderBy(ancestry => Loc.GetString(ancestry.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            covered.Add(ancestry.Species);
            options.Add(new Pf2eListEntry
            {
                Id = ancestry.Species,
                Name = Loc.GetString(ancestry.Name),
                Rarity = ancestry.Rarity,
                Traits = JoinLoc(ancestry.Traits),
                Description = Loc.GetString(ancestry.Description),
                HitPoints = Stat("cf-character-builder-stat-hp", ancestry.HitPoints.ToString()),
                Size = Stat("cf-character-builder-stat-size", Loc.GetString($"cf-size-{ancestry.Size.ToString().ToLower()}")),
                Speed = Stat("cf-character-builder-stat-speed", $"{ancestry.Speed} {Loc.GetString("cf-character-builder-feet")}"),
                Boosts = Stat("cf-character-builder-stat-boosts", FormatBoosts(ancestry.AbilityBoosts)),
                Flaws = Stat("cf-character-builder-stat-flaws", FormatAbilities(ancestry.AbilityFlaws)),
                Languages = Stat("cf-character-builder-stat-languages", JoinLoc(ancestry.Languages)),
            });
        }

        foreach (var species in _prototypeManager.EnumeratePrototypes<SpeciesPrototype>().Where(species => species.RoundStart && !covered.Contains(species.ID)).OrderBy(species => Loc.GetString(species.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            var none = Loc.GetString("cf-character-builder-none");
            options.Add(new Pf2eListEntry
            {
                Id = species.ID,
                Name = Loc.GetString(species.Name),
                Rarity = CFRarity.Uncommon,
                Traits = Loc.GetString(species.Name),
                Description = "",
                HitPoints = Stat("cf-character-builder-stat-hp", none),
                Size = Stat("cf-character-builder-stat-size", none),
                Speed = Stat("cf-character-builder-stat-speed", none),
                Boosts = Stat("cf-character-builder-stat-boosts", none),
                Flaws = Stat("cf-character-builder-stat-flaws", none),
                Languages = Stat("cf-character-builder-stat-languages", none),
            });
        }

        SelectionView.Show(
            Loc.GetString("cf-character-builder-title-ancestry"),
            Loc.GetString("cf-character-builder-filter-all-ancestries"),
            options,
            selected);
        OpenPicker();
    }

    private void ShowBackgroundSelection()
    {
        _selection = BuilderSelection.Background;
        _prdUrl = BackgroundPrd;
        _pendingId = _backgroundId;
        var options = new List<Pf2eListEntry>();

        foreach (var background in _prototypeManager.EnumeratePrototypes<CFBackgroundPrototype>().OrderBy(background => Loc.GetString(background.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new Pf2eListEntry
            {
                Id = background.ID,
                Name = Loc.GetString(background.Name),
                Rarity = background.Rarity,
                Traits = Loc.GetString("cf-character-builder-skills", ("skills", FormatSkills(background.SkillBoosts, background.TrainedSkills))),
                Description = Loc.GetString(background.Description),
                Boosts = Stat("cf-character-builder-stat-boosts", FormatBoosts(background.AbilityBoosts)),
            });
        }

        SelectionView.Show(
            Loc.GetString("cf-character-builder-title-background"),
            Loc.GetString("cf-character-builder-filter-all-backgrounds"),
            options,
            _backgroundId);
        OpenPicker();
    }

    private void ShowClassSelection()
    {
        _selection = BuilderSelection.Class;
        _prdUrl = ClassPrd;
        _pendingId = _classId;
        var options = new List<Pf2eListEntry>();

        foreach (var cls in _prototypeManager.EnumeratePrototypes<CFClassPrototype>().OrderBy(cls => Loc.GetString(cls.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new Pf2eListEntry
            {
                Id = cls.ID,
                Name = Loc.GetString(cls.Name),
                Rarity = cls.Rarity,
                Traits = Loc.GetString("cf-character-builder-class-key-ability", ("ability", AbilityName(cls.KeyAbility))),
                Description = Loc.GetString(cls.Description),
                HitPoints = Stat("cf-character-builder-stat-hp", cls.HitPoints.ToString()),
                Boosts = Stat("cf-character-builder-stat-boosts", FormatBoosts(cls.AbilityBoosts)),
                Rules = string.Join('\n', new[]
                {
                    Loc.GetString("cf-character-builder-class-hp-ability", ("ability", AbilityName(cls.HitPointsAbility))),
                    DescribeProficiencies(cls.Proficiencies),
                }.Where(line => !string.IsNullOrWhiteSpace(line))),
            });
        }

        SelectionView.Show(
            Loc.GetString("cf-character-builder-title-class"),
            Loc.GetString("cf-character-builder-filter-all-classes"),
            options,
            _classId);
        OpenPicker();
    }

    private void UpdateBuilderSummary()
    {
        var ancestry = Loc.GetString("cf-character-builder-none");
        if (Profile != null && _prototypeManager.TryIndex(Profile.Species, out SpeciesPrototype? species))
            ancestry = Loc.GetString(species.Name);

        var background = Loc.GetString("cf-character-builder-none");
        if (_backgroundId != null && _prototypeManager.TryIndex<CFBackgroundPrototype>(_backgroundId, out var backgroundProto))
            background = Loc.GetString(backgroundProto.Name);

        var clsName = Loc.GetString("cf-character-builder-none");
        if (_classId != null && _prototypeManager.TryIndex<CFClassPrototype>(_classId, out var classProto))
            clsName = Loc.GetString(classProto.Name);

        var jobName = Loc.GetString("cf-character-builder-none");
        if (Profile != null)
        {
            var highPriorityJob = Profile.JobPriorities.FirstOrDefault(priority => priority.Value == JobPriority.High).Key;
            if (!string.IsNullOrEmpty(highPriorityJob) && _prototypeManager.TryIndex<JobPrototype>(highPriorityJob, out var job))
                jobName = job.LocalizedName;
        }

        AncestryButton.Text = $"{Loc.GetString("cf-character-builder-ancestry")}: {ancestry}";
        BackgroundButton.Text = $"{Loc.GetString("cf-character-builder-background")}: {background}";
        ClassButton.Text = $"{Loc.GetString("cf-character-builder-class")}: {clsName}";
        JobButton.Text = $"{Loc.GetString("cf-character-builder-job")}: {jobName}";
        UpdateHeaderStats();
    }

    private void UpdateHeaderStats()
    {
        CFAncestryPrototype? ancestry = null;
        if (Profile != null)
        {
            foreach (var proto in _prototypeManager.EnumeratePrototypes<CFAncestryPrototype>())
            {
                if (proto.Species != Profile.Species)
                    continue;

                ancestry = proto;
                break;
            }
        }

        CFClassPrototype? cls = null;
        if (_classId != null)
            _prototypeManager.TryIndex(_classId, out cls);

        CFBackgroundPrototype? background = null;
        if (_backgroundId != null)
            _prototypeManager.TryIndex(_backgroundId, out background);

        var scores = Enum.GetValues<CFAbility>().ToDictionary(ability => ability, _ => 10);
        if (ancestry != null)
        {
            foreach (var boost in ancestry.AbilityBoosts)
                ApplyBoost(scores, boost);
            foreach (var flaw in ancestry.AbilityFlaws)
                scores[flaw] -= 2;
        }

        if (cls != null)
        {
            foreach (var boost in cls.AbilityBoosts)
                ApplyBoost(scores, boost);
        }

        if (background != null)
        {
            foreach (var boost in background.AbilityBoosts)
                ApplyBoost(scores, boost);
        }

        var size = ancestry == null
            ? Loc.GetString("cf-character-builder-none")
            : Loc.GetString($"cf-size-{ancestry.Size.ToString().ToLower()}");
        var speed = ancestry == null
            ? Loc.GetString("cf-character-builder-none")
            : $"{ancestry.Speed} {Loc.GetString("cf-character-builder-feet")}";

        SizeLabel.Text = Loc.GetString("cf-character-builder-size", ("size", size));
        SpeedLabel.Text = Loc.GetString("cf-character-builder-speed", ("speed", speed));
        StrLabel.Text = Loc.GetString("cf-character-builder-str", ("score", scores[CFAbility.Strength]));
        DexLabel.Text = Loc.GetString("cf-character-builder-dex", ("score", scores[CFAbility.Dexterity]));
        ConLabel.Text = Loc.GetString("cf-character-builder-con", ("score", scores[CFAbility.Constitution]));
        IntLabel.Text = Loc.GetString("cf-character-builder-int", ("score", scores[CFAbility.Intelligence]));
        WisLabel.Text = Loc.GetString("cf-character-builder-wis", ("score", scores[CFAbility.Wisdom]));
        ChaLabel.Text = Loc.GetString("cf-character-builder-cha", ("score", scores[CFAbility.Charisma]));

        var conMod = AbilityMod(scores[CFAbility.Constitution]);
        var maxHp = (ancestry?.HitPoints ?? 0) + (cls?.HitPoints ?? 0) + conMod;
        if (maxHp < 0)
            maxHp = 0;

        HpSummary.Text = Loc.GetString("cf-character-builder-hp-summary", ("current", maxHp), ("max", maxHp));
        AcValue.Text = (10 + AbilityMod(scores[CFAbility.Dexterity])).ToString();

        var trainedSaves = cls?.Proficiencies.SavingThrows.Trained ?? new List<string>();
        FortitudeLabel.Text = SaveLine("cf-character-builder-save-fortitude", trainedSaves.Contains("Fortitude"), AbilityMod(scores[CFAbility.Constitution]));
        ReflexLabel.Text = SaveLine("cf-character-builder-save-reflex", trainedSaves.Contains("Reflex"), AbilityMod(scores[CFAbility.Dexterity]));
        WillLabel.Text = SaveLine("cf-character-builder-save-will", trainedSaves.Contains("Will"), AbilityMod(scores[CFAbility.Wisdom]));
    }

    private static void ApplyBoost(Dictionary<CFAbility, int> scores, CFAbilityBoost boost)
    {
        if (boost == CFAbilityBoost.Free)
            return;

        scores[(CFAbility)boost] += 2;
    }

    private static int AbilityMod(int score)
    {
        return (int)Math.Floor((score - 10) / 2.0);
    }

    private static string SaveLine(string saveLoc, bool trained, int abilityMod)
    {
        var bonus = abilityMod + (trained ? 3 : 0);
        var rank = Loc.GetString(trained
            ? "cf-character-builder-prof-trained-short"
            : "cf-character-builder-prof-untrained-short");
        var text = bonus >= 0 ? $"+{bonus}" : bonus.ToString();
        return Loc.GetString("cf-character-builder-save-line",
            ("rank", rank),
            ("bonus", text),
            ("save", Loc.GetString(saveLoc)));
    }

    public void RefreshEquipmentTabs()
    {
        var collection = IoCManager.Instance;
        var session = _playerManager.LocalSession;
        HumanoidCharacterProfile? GetProfile() => Profile;

        void OnChanged(HumanoidCharacterProfile updated)
        {
            Profile = updated;
            SetDirty();
            ReloadPreview();
        }

        WeaponsTab.Rebuild(Profile, JobOverride, session, collection, _prototypeManager, GetProfile, OnChanged);
        DefenseTab.Rebuild(Profile, JobOverride, session, collection, _prototypeManager, GetProfile, OnChanged);
        GearTab.Rebuild(Profile, JobOverride, session, collection, _prototypeManager, GetProfile, OnChanged);
        SpellsTab.Rebuild(Profile, JobOverride, session, collection, _prototypeManager, GetProfile, OnChanged);
    }

    public void FocusGearTab(JobPrototype job)
    {
        ClosePicker();
        JobOverride = job;
        RefreshEquipmentTabs();
        TabContainer.CurrentTab = GearTabIndex;
        ReloadPreview();
    }

    private static string Stat(string locId, string value)
    {
        return Loc.GetString(locId, ("value", value));
    }

    private static string JoinLoc(IEnumerable<LocId> ids)
    {
        var names = ids.Select(id => Loc.GetString(id)).ToArray();
        return names.Length == 0 ? Loc.GetString("cf-character-builder-none") : string.Join(", ", names);
    }

    private static string FormatAbilities(IEnumerable<CFAbility> abilities)
    {
        var names = abilities.Select(AbilityName).ToArray();
        return names.Length == 0 ? Loc.GetString("cf-character-builder-none") : string.Join(", ", names);
    }

    private static string AbilityName(CFAbility ability)
    {
        return Loc.GetString($"cf-ability-{ability.ToString().ToLower()}");
    }

    private static string FormatBoosts(IEnumerable<CFAbilityBoost> boosts)
    {
        var names = boosts.Select(boost => boost == CFAbilityBoost.Free
            ? Loc.GetString("cf-character-builder-free")
            : AbilityName((CFAbility)boost)).ToArray();
        return names.Length == 0 ? Loc.GetString("cf-character-builder-none") : string.Join(", ", names);
    }

    private string FormatSkills(IEnumerable<ProtoId<CFAbilitySkillPrototype>> boosts, IEnumerable<ProtoId<CFAbilitySkillPrototype>> trained)
    {
        var names = boosts.Concat(trained).Distinct().Select(id =>
        {
            if (_prototypeManager.TryIndex(id, out CFAbilitySkillPrototype? skill))
                return Loc.GetString(skill.Name);
            return id.ToString();
        }).ToArray();
        return names.Length == 0 ? Loc.GetString("cf-character-builder-none") : string.Join(", ", names);
    }

    private static string DescribeProficiencies(CFClassProficiencies proficiencies)
    {
        var lines = new List<string>();
        AppendProficiency(lines, Loc.GetString("cf-character-builder-prof-perception"), proficiencies.Perception);
        AppendProficiency(lines, Loc.GetString("cf-character-builder-prof-saves"), proficiencies.SavingThrows);
        AppendProficiency(lines, Loc.GetString("cf-character-builder-prof-skills"), proficiencies.Skills);
        AppendProficiency(lines, Loc.GetString("cf-character-builder-prof-attacks"), proficiencies.Attacks);
        AppendProficiency(lines, Loc.GetString("cf-character-builder-prof-defenses"), proficiencies.Defenses);
        return string.Join('\n', lines);
    }

    private static void AppendProficiency(List<string> lines, string label, CFProficiencyList list)
    {
        var trained = list.Trained;
        if (trained.Count == 0)
            return;

        lines.Add(Loc.GetString("cf-character-builder-trained", ("items", $"{label}: {string.Join(", ", trained)}")));
    }
}
