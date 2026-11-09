using Content.Shared.CF.Languages;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Client.CF.Lobby.UI.CharacterBuilder;
using Content.Shared.CF.Character;
using Content.Shared.CF.Preferences;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private const int DetailsTab = 0;
    private const int ShopTabIndex = 1;
    private const int WeaponsTabIndex = 2;
    private const int DefenseTabIndex = 3;
    private const int GearTabIndex = 4;
    private const int SpellsTabIndex = 5;
    private const int PetTabIndex = 6;

    private enum BuilderSelection
    {
        Ancestry,
        Background,
        Class,
        Job,
        Heritage,
        Sprite,
        AncestryFeat,
        ClassFeat,
        ClassFeatLevel2,
        ClassFeatLevel4,
        AncestryFeatLevel5,
        SkillFeatLevel2,
        GeneralFeatLevel3,
        SkillFeatLevel4,
        BonusSkillFeat,
        ClassChoice,
        Deity,
    }

    private enum SkillAssignMode
    {
        OptionChoice,
        Language,
        IncreaseLevel3,
        IncreaseLevel5,
        BonusIncrease,
        Background,
        Class,
    }

    /// <summary>Which level's extra slot <see cref="BuilderSelection.BonusSkillFeat"/> or the bonus increase is for.</summary>
    private int _bonusLevel;

    /// <summary>The character has been accepted, so everything past the first step is shown.</summary>
    private bool _setupAccepted;

    private BuilderSelection _selection = BuilderSelection.Ancestry;
    private SkillAssignMode _skillMode;

    /// <summary>The option whose choices the skill view is showing.</summary>
    private string _optionSource = string.Empty;

    /// <summary>True while the boost view shows level 5's boosts, not level 1's.</summary>
    private bool _levelBoosts;
    private bool _pickerOpen;
    private string? _pendingId;
    private string? _prdUrl;

    private const string AncestryPrd = "https://2e.aonprd.com/Ancestries.aspx";
    private const string BackgroundPrd = "https://2e.aonprd.com/Backgrounds.aspx";
    private const string ClassPrd = "https://2e.aonprd.com/Classes.aspx";

    private static readonly CFAbility[] AllAbilities = Enum.GetValues<CFAbility>();

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

        BoostView.OnClosed += ClosePicker;
        BoostView.OnAccept += AcceptBoostAssignment;
        SetAbilitiesButton.OnPressed += _ => ShowBoostAssignment();
        HeritageSlot.OnPressed += _ => ShowHeritageSelection();
        ClassFeatSlot.OnPressed += _ => ShowClassFeatSelection(1);
        Level2FeatSlot.OnPressed += _ => ShowClassFeatSelection(2);
        Level4FeatSlot.OnPressed += _ => ShowClassFeatSelection(4);
        AncestryFeatSlot.OnPressed += _ => ShowAncestryFeatSelection(1);
        Level5FeatSlot.OnPressed += _ => ShowAncestryFeatSelection(5);
        SpriteSlot.OnPressed += _ => ShowSpriteSelection();
        DeitySlot.OnPressed += _ => ShowDeitySelection();

        SkillView.OnClosed += ClosePicker;
        SkillView.OnAccept += AcceptSkillAssignment;
        SkillTrainingButton.OnPressed += _ => ShowSkillTraining();
        ClassSkillButton.OnPressed += _ => ShowClassSkill();
        LanguagesButton.OnPressed += _ => ShowLanguages();
        Level2SkillFeatSlot.OnPressed += _ => ShowGeneralFeatSelection(BuilderSelection.SkillFeatLevel2, 2);
        Level3GeneralFeatSlot.OnPressed += _ => ShowGeneralFeatSelection(BuilderSelection.GeneralFeatLevel3, 3);
        Level4SkillFeatSlot.OnPressed += _ => ShowGeneralFeatSelection(BuilderSelection.SkillFeatLevel4, 4);
        Level1BonusSkillFeatSlot.OnPressed += _ => ShowGeneralFeatSelection(BuilderSelection.BonusSkillFeat, 1);
        Level3BonusSkillFeatSlot.OnPressed += _ => ShowGeneralFeatSelection(BuilderSelection.BonusSkillFeat, 3);
        Level5BonusSkillFeatSlot.OnPressed += _ => ShowGeneralFeatSelection(BuilderSelection.BonusSkillFeat, 5);
        Level2BonusSkillIncreaseSlot.OnPressed += _ => ShowSkillIncrease(2, bonus: true);
        Level4BonusSkillIncreaseSlot.OnPressed += _ => ShowSkillIncrease(4, bonus: true);
        Level3SkillIncreaseSlot.OnPressed += _ => ShowSkillIncrease(3);
        Level5SkillIncreaseSlot.OnPressed += _ => ShowSkillIncrease(5);
        Level5BoostsSlot.OnPressed += _ => ShowLevelBoosts();

        TabContainer.SetTabTitle(DetailsTab, Loc.GetString("cf-character-builder-details-tab"));
        TabContainer.SetTabTitle(ShopTabIndex, Loc.GetString("cf-character-builder-shop-tab"));
        TabContainer.SetTabTitle(WeaponsTabIndex, Loc.GetString("cf-character-builder-weapons-tab"));
        TabContainer.SetTabTitle(DefenseTabIndex, Loc.GetString("cf-character-builder-defense-tab"));
        TabContainer.SetTabTitle(GearTabIndex, Loc.GetString("cf-character-builder-gear-tab"));
        TabContainer.SetTabTitle(SpellsTabIndex, Loc.GetString("cf-character-builder-spells-tab"));
        TabContainer.SetTabTitle(PetTabIndex, Loc.GetString("cf-character-builder-pet-tab"));

        AcceptCharacterButton.OnPressed += _ => AcceptCharacter();

        UpdateBuilderSummary();
        ApplySetupStage();
        Level2Toggle.OnPressed += _ => Level2Body.Visible = Level2Toggle.Pressed;
        Level3Toggle.OnPressed += _ => Level3Body.Visible = Level3Toggle.Pressed;
        Level4Toggle.OnPressed += _ => Level4Body.Visible = Level4Toggle.Pressed;
        Level5Toggle.OnPressed += _ => Level5Body.Visible = Level5Toggle.Pressed;
    }

    /// <summary>Levels and the tabs stay hidden until the ancestry, background, class and job are chosen and accepted.</summary>
    private void ApplySetupStage()
    {
        LevelsHeading.Visible = _setupAccepted;
        LevelsScroll.Visible = _setupAccepted;
        if (!SelectionView.Visible && !BoostView.Visible && !SkillView.Visible && !JobPane.Visible)
            EditorPane.Visible = _setupAccepted;
    }

    private void AcceptCharacter()
    {
        if (Profile == null || CFCharacterRequirements.Missing(_prototypeManager, Profile).Count > 0)
            return;

        ClosePicker();
        _setupAccepted = true;
        ApplySetupStage();
        Save?.Invoke();
    }

    /// <summary>Accept only lights up once there is nothing left to choose, and says what is if there is.</summary>
    private void UpdateAcceptButton()
    {
        var missing = Profile == null
            ? new List<LocId>()
            : CFCharacterRequirements.Missing(_prototypeManager, Profile);

        AcceptCharacterButton.Disabled = Profile == null || missing.Count > 0;
        AcceptCharacterButton.ToolTip = missing.Count == 0
            ? null
            : Loc.GetString("cf-character-missing-tooltip", ("missing", string.Join(", ", missing.Select(id => Loc.GetString(id)))));
    }

    private CFCharacterBuildData CurrentBuild()
    {
        return Profile?.CFBuild.Clone() ?? new CFCharacterBuildData();
    }

    private void SetBuild(CFCharacterBuildData build)
    {
        if (Profile == null)
            return;

        // Any change here can cost a later feat its skill rank.
        CFSkills.DropUnqualifiedFeats(_prototypeManager, build, CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species)?.ID);
        Profile = Profile.WithCFBuild(build);
        SetDirty();
        UpdateBuilderSummary();
    }

    private void AcceptPicker()
    {
        var id = _pendingId;
        ClosePicker();
        if (id == null)
            return;

        OnBuilderOptionSelected(id);

        // A new option that needs a cantrip or skill asks for it straight away. It can be changed later from its button.
        if (Profile != null
            && OptionSlots(CurrentBuild()).Any(slot => slot.Source == id)
            && CurrentBuild().OptionPicks.All(pick => pick.Source != id))
        {
            ShowOptionChoices(id);
        }
    }

    private List<CFOptionSlot> OptionSlots(CFCharacterBuildData build)
    {
        return Profile == null
            ? new List<CFOptionSlot>()
            : CFOptionChoices.Slots(_prototypeManager, build, CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species)?.ID);
    }

    /// <summary>One button for each option in the build that has something to choose.</summary>
    private void RefreshOptionChoiceSlots(CFCharacterBuildData build)
    {
        OptionChoiceSlots.RemoveAllChildren();
        foreach (var group in OptionSlots(build).GroupBy(slot => slot.Source))
        {
            var source = group.Key;
            var picks = group
                .Select(slot => build.OptionPicks.FirstOrDefault(p => p.Source == slot.Source && p.Slot == slot.Slot))
                .Where(pick => pick != null)
                .Select(pick => OptionLabel(pick!.Value))
                .ToList();
            var button = new Button
            {
                Text = Loc.GetString(
                    "cf-character-builder-option-choice",
                    ("name", Loc.GetString(group.First().SourceName)),
                    ("picks", picks.Count == group.Count()
                        ? string.Join(", ", picks)
                        : Loc.GetString("cf-character-builder-option-choose"))),
                HorizontalExpand = true,
            };
            button.OnPressed += _ => ShowOptionChoices(source);
            OptionChoiceSlots.AddChild(button);
        }
    }

    private string OptionLabel(string value)
    {
        if (_prototypeManager.TryIndex<CFSpellPrototype>(value, out var spell))
            return spell.Name;

        if (_prototypeManager.TryIndex<CFLanguagePrototype>(value, out var language))
            return Loc.GetString(language.Name);

        return SkillLabelMap().GetValueOrDefault(value, value);
    }

    private void ShowOptionChoices(string source)
    {
        var build = CurrentBuild();
        var slots = OptionSlots(build).Where(slot => slot.Source == source).ToList();
        if (Profile == null || slots.Count == 0)
            return;

        _skillMode = SkillAssignMode.OptionChoice;
        _optionSource = source;

        // Skills the character already has are not offered, and neither is one this option is about to replace.
        var ownPicks = build.OptionPicks.Where(pick => pick.Source == source).Select(pick => pick.Value).ToHashSet();
        var ancestry = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species)?.ID;
        var trained = CFSkills.CollectTrainedSkills(_prototypeManager, build, ancestry, build.Class?.Id, CFExperienceComponent.MaxLevel).ToHashSet();
        if (build.Class is { } classId && _prototypeManager.TryIndex(classId, out CFClassPrototype? cls))
            trained.UnionWith(cls.Proficiencies.Skills.Trained);

        var definitions = new List<SkillSlotDefinition>();
        foreach (var slot in slots)
        {
            var allowed = CFOptionChoices.Allowed(_prototypeManager, slot.Choice)
                .Where(id => slot.Choice.Kind != CFOptionChoiceKind.Skill || ownPicks.Contains(id) || !trained.Contains(id))
                .OrderBy(OptionLabel, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            definitions.Add(new SkillSlotDefinition
            {
                Id = slot.Slot.ToString(),
                Label = Loc.GetString(slot.Choice.Kind switch
                {
                    CFOptionChoiceKind.Cantrip => "cf-character-builder-option-cantrip",
                    CFOptionChoiceKind.WeaponGroup => "cf-character-builder-option-weapon-group",
                    CFOptionChoiceKind.Language => "cf-character-builder-option-language",
                    _ => "cf-character-builder-option-skill",
                }),
                Allowed = allowed,
                Labels = allowed.ToDictionary(id => id, OptionLabel),
                Selected = build.OptionPicks.FirstOrDefault(p => p.Source == source && p.Slot == slot.Slot)?.Value,
            });
        }

        SkillView.Show(
            Loc.GetString(slots[0].SourceName),
            Loc.GetString("cf-character-builder-option-help"),
            definitions);
        OpenSkillView();
    }

    private void ClosePicker()
    {
        _pickerOpen = false;
        _pendingId = null;
        SelectionView.Visible = false;
        BoostView.Visible = false;
        SkillView.Visible = false;
        JobPane.Visible = false;
        EditorPane.Visible = _setupAccepted;
        AncestryButton.Pressed = false;
        BackgroundButton.Pressed = false;
        ClassButton.Pressed = false;
        JobButton.Pressed = false;
        HeritageSlot.Pressed = false;
        SpriteSlot.Pressed = false;
        AncestryFeatSlot.Pressed = false;
        ClassFeatSlot.Pressed = false;
        Level2FeatSlot.Pressed = false;
        Level4FeatSlot.Pressed = false;
        Level5FeatSlot.Pressed = false;
    }

    private void OpenPicker()
    {
        EditorPane.Visible = false;
        JobPane.Visible = false;
        BoostView.Visible = false;
        SkillView.Visible = false;
        SelectionView.Visible = true;
        _pickerOpen = true;
    }

    private void OpenBoostView()
    {
        EditorPane.Visible = false;
        JobPane.Visible = false;
        SelectionView.Visible = false;
        SkillView.Visible = false;
        BoostView.Visible = true;
        _pickerOpen = true;
    }

    private void OpenSkillView()
    {
        EditorPane.Visible = false;
        JobPane.Visible = false;
        SelectionView.Visible = false;
        BoostView.Visible = false;
        SkillView.Visible = true;
        _pickerOpen = true;
    }

    private void ShowJobSelection()
    {
        _selection = BuilderSelection.Job;
        _pickerOpen = false;
        EditorPane.Visible = false;
        SelectionView.Visible = false;
        BoostView.Visible = false;
        SkillView.Visible = false;
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
            {
                var current = Profile == null ? null : CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species);
                var picked = CFAttributeMath.FindAncestry(_prototypeManager, id);
                if (picked == null)
                    SetSpecies(id);
                else if (current?.ID != picked.ID)
                    SetSpecies(picked.Species);

                var build = CurrentBuild();
                if (current?.ID != picked?.ID)
                    build.AncestryFreeBoosts.Clear();
                if (build.Heritage != null
                    && _prototypeManager.TryIndex(build.Heritage.Value, out CFHeritagePrototype? heritage)
                    && !heritage.Fits(picked?.ID))
                    build.Heritage = null;
                if (build.AncestryFeat != null
                    && _prototypeManager.TryIndex(build.AncestryFeat.Value, out CFFeatPrototype? feat)
                    && !feat.Fits(picked?.ID, 1))
                    build.AncestryFeat = null;
                if (build.AncestryFeatLevel5 != null
                    && _prototypeManager.TryIndex(build.AncestryFeatLevel5.Value, out CFFeatPrototype? feat5)
                    && !feat5.Fits(picked?.ID, 5))
                    build.AncestryFeatLevel5 = null;
                SetBuild(build);
                break;
            }
            case BuilderSelection.Sprite:
                SetSpecies(id);
                break;
            case BuilderSelection.Background:
            {
                var next = CurrentBuild();
                next.Background = id;
                next.BackgroundBoost = null;
                next.BackgroundFreeBoost = null;
                next.BackgroundTrainedSkills.Clear();
                SetBuild(next);
                break;
            }
            case BuilderSelection.AncestryFeat:
            {
                var next = CurrentBuild();
                next.AncestryFeat = id;
                if (next.AncestryFeatLevel5?.Id == id)
                    next.AncestryFeatLevel5 = null;
                SetBuild(next);
                break;
            }
            case BuilderSelection.Heritage:
            {
                var next = CurrentBuild();
                next.Heritage = id;
                if (_prototypeManager.TryIndex<CFHeritagePrototype>(id, out var pickedHeritage))
                    next.ClassTrainedSkills.RemoveAll(skill => pickedHeritage.TrainedSkills.Contains(skill));
                SetBuild(next);
                if (Profile != null
                    && CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species) is { } ancestry
                    && ancestry.Sprites.FirstOrDefault(sprite => sprite.Species == Profile.Species) is { } current
                    && current.Heritage is { } required
                    && required.Id != id)
                    SetSpecies(ancestry.Species);
                break;
            }
            case BuilderSelection.Class:
            {
                var next = CurrentBuild();
                next.Class = id;
                next.ClassTrainedSkills.Clear();
                if (next.ClassFeat != null
                    && _prototypeManager.TryIndex(next.ClassFeat.Value, out CFFeatPrototype? classFeat)
                    && !classFeat.FitsClass(id, 1))
                    next.ClassFeat = null;
                if (next.ClassFeatLevel2 != null
                    && _prototypeManager.TryIndex(next.ClassFeatLevel2.Value, out CFFeatPrototype? classFeatLevel2)
                    && !classFeatLevel2.FitsClass(id, 2))
                    next.ClassFeatLevel2 = null;
                if (next.ClassFeatLevel4 != null
                    && _prototypeManager.TryIndex(next.ClassFeatLevel4.Value, out CFFeatPrototype? classFeatLevel4)
                    && !classFeatLevel4.FitsClass(id, 4))
                    next.ClassFeatLevel4 = null;
                CFClassChoices.Sanitize(_prototypeManager, next);
                SetBuild(next);
                break;
            }
            case BuilderSelection.ClassChoice:
            {
                var next = CurrentBuild();
                CFClassChoices.Assign(_prototypeManager, next, id);
                SetBuild(next);
                break;
            }
            case BuilderSelection.Deity:
            {
                var next = CurrentBuild();
                CFClassChoices.AssignDeity(_prototypeManager, next, id);
                SetBuild(next);
                break;
            }
            case BuilderSelection.ClassFeat:
                SetBuild(AssignClassFeat(CurrentBuild(), 1, id));
                break;
            case BuilderSelection.ClassFeatLevel2:
                SetBuild(AssignClassFeat(CurrentBuild(), 2, id));
                break;
            case BuilderSelection.ClassFeatLevel4:
                SetBuild(AssignClassFeat(CurrentBuild(), 4, id));
                break;
            case BuilderSelection.SkillFeatLevel2:
            {
                var next = CurrentBuild();
                next.SkillFeatLevel2 = id;
                SetBuild(next);
                break;
            }
            case BuilderSelection.GeneralFeatLevel3:
            {
                var next = CurrentBuild();
                next.GeneralFeatLevel3 = id;
                SetBuild(next);
                break;
            }
            case BuilderSelection.SkillFeatLevel4:
            {
                var next = CurrentBuild();
                next.SkillFeatLevel4 = id;
                SetBuild(next);
                break;
            }
            case BuilderSelection.BonusSkillFeat:
            {
                var next = CurrentBuild();
                next.BonusSkillFeats[_bonusLevel] = id;
                SetBuild(next);
                break;
            }
            case BuilderSelection.AncestryFeatLevel5:
            {
                var next = CurrentBuild();
                next.AncestryFeatLevel5 = id;
                if (next.AncestryFeat?.Id == id)
                    next.AncestryFeat = null;
                SetBuild(next);
                break;
            }
        }
    }

    private void ShowAncestrySelection()
    {
        _selection = BuilderSelection.Ancestry;
        _prdUrl = AncestryPrd;
        var currentAncestry = Profile == null ? null : CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species);
        var selected = currentAncestry?.Species.Id ?? Profile?.Species.Id;
        _pendingId = selected;
        var covered = new HashSet<string>();
        var options = new List<Pf2eListEntry>();

        foreach (var ancestry in _prototypeManager.EnumeratePrototypes<CFAncestryPrototype>().OrderBy(ancestry => Loc.GetString(ancestry.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            covered.Add(ancestry.Species);
            foreach (var sprite in ancestry.Sprites)
                covered.Add(sprite.Species);
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
                Languages = Stat("cf-character-builder-stat-languages", JoinLanguages(ancestry.Languages)),
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
        var selected = Profile?.CFBuild.Background?.Id;
        _pendingId = selected;
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
            selected);
        OpenPicker();
    }

    private void ShowClassSelection()
    {
        _selection = BuilderSelection.Class;
        _prdUrl = ClassPrd;
        var selected = Profile?.CFBuild.Class?.Id;
        _pendingId = selected;
        var options = new List<Pf2eListEntry>();

        foreach (var cls in _prototypeManager.EnumeratePrototypes<CFClassPrototype>().OrderBy(cls => Loc.GetString(cls.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new Pf2eListEntry
            {
                Id = cls.ID,
                Name = Loc.GetString(cls.Name),
                Rarity = cls.Rarity,
                Traits = Loc.GetString("cf-character-builder-class-key-ability", ("ability", AbilityName(cls.KeyAbility))),
                Description = DescribeClass(cls),
                HitPoints = Stat("cf-character-builder-stat-hp", cls.HitPoints.ToString()),
                Boosts = Stat("cf-character-builder-stat-boosts", AbilityName(cls.KeyAbility)),
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
            selected);
        OpenPicker();
    }

    private static string DescribeClass(CFClassPrototype cls)
    {
        var text = Loc.GetString(cls.Description);
        if (cls.Features.Count == 0)
            return text;

        var lines = new List<string> { text };
        foreach (var feature in cls.Features.OrderBy(feature => feature.Level).ThenBy(feature => feature.Name, StringComparer.Ordinal))
        {
            var name = feature.Unfinished
                ? Loc.GetString("cf-character-builder-feat-tba", ("name", feature.Name))
                : feature.Name;
            lines.Add($"Level {feature.Level}: {name}. {feature.Description}");
        }

        return string.Join("\n", lines);
    }

    private static CFCharacterBuildData AssignClassFeat(CFCharacterBuildData build, int slot, string id)
    {
        if (slot == 2)
            build.ClassFeatLevel2 = id;
        else if (slot >= 4)
            build.ClassFeatLevel4 = id;
        else
            build.ClassFeat = id;

        if (slot != 1 && build.ClassFeat?.Id == id)
            build.ClassFeat = null;
        if (slot != 2 && build.ClassFeatLevel2?.Id == id)
            build.ClassFeatLevel2 = null;
        if (slot < 4 && build.ClassFeatLevel4?.Id == id)
            build.ClassFeatLevel4 = null;
        return build;
    }

    private void ShowAncestryFeatSelection(int maxLevel)
    {
        if (Profile == null)
            return;

        _selection = maxLevel <= 1 ? BuilderSelection.AncestryFeat : BuilderSelection.AncestryFeatLevel5;
        _prdUrl = AncestryPrd;
        AncestryButton.Pressed = false;
        BackgroundButton.Pressed = false;
        ClassButton.Pressed = false;
        JobButton.Pressed = false;
        HeritageSlot.Pressed = false;
        SpriteSlot.Pressed = false;
        AncestryFeatSlot.Pressed = maxLevel <= 1;
        ClassFeatSlot.Pressed = false;
        Level2FeatSlot.Pressed = false;
        Level4FeatSlot.Pressed = false;
        Level5FeatSlot.Pressed = maxLevel > 1;

        var build = CurrentBuild();
        var ancestry = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species);
        var selected = maxLevel <= 1 ? build.AncestryFeat?.Id : build.AncestryFeatLevel5?.Id;
        var other = maxLevel <= 1 ? build.AncestryFeatLevel5?.Id : build.AncestryFeat?.Id;
        _pendingId = selected;
        var options = new List<Pf2eListEntry>();

        foreach (var feat in _prototypeManager.EnumeratePrototypes<CFFeatPrototype>()
                     .Where(feat => ancestry != null && feat.Fits(ancestry.ID, maxLevel) && feat.ID != other)
                     .OrderBy(feat => feat.Level)
                     .ThenBy(feat => Loc.GetString(feat.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new Pf2eListEntry
            {
                Id = feat.ID,
                Name = FeatName(feat),
                Rarity = feat.Rarity,
                Traits = Loc.GetString("cf-character-builder-feat-level", ("level", feat.Level)),
                Description = Loc.GetString(feat.Description),
            });
        }

        SelectionView.Show(
            Loc.GetString("cf-character-builder-title-ancestry-feat"),
            Loc.GetString("cf-character-builder-filter-all-ancestry-feats"),
            options,
            selected);
        OpenPicker();
    }

    private void ShowClassFeatSelection(int maxLevel)
    {
        if (Profile == null)
            return;

        _selection = maxLevel switch
        {
            <= 1 => BuilderSelection.ClassFeat,
            2 => BuilderSelection.ClassFeatLevel2,
            _ => BuilderSelection.ClassFeatLevel4,
        };
        _prdUrl = AncestryPrd;
        AncestryButton.Pressed = false;
        BackgroundButton.Pressed = false;
        ClassButton.Pressed = false;
        JobButton.Pressed = false;
        HeritageSlot.Pressed = false;
        SpriteSlot.Pressed = false;
        AncestryFeatSlot.Pressed = false;
        Level5FeatSlot.Pressed = false;
        ClassFeatSlot.Pressed = maxLevel <= 1;
        Level2FeatSlot.Pressed = maxLevel == 2;
        Level4FeatSlot.Pressed = maxLevel >= 4;

        var build = CurrentBuild();
        var classId = build.Class?.Id;
        var selected = maxLevel switch
        {
            <= 1 => build.ClassFeat?.Id,
            2 => build.ClassFeatLevel2?.Id,
            _ => build.ClassFeatLevel4?.Id,
        };
        var taken = new HashSet<string>();
        if (maxLevel != 1 && build.ClassFeat != null)
            taken.Add(build.ClassFeat.Value.Id);
        if (maxLevel != 2 && build.ClassFeatLevel2 != null)
            taken.Add(build.ClassFeatLevel2.Value.Id);
        if (maxLevel < 4 && build.ClassFeatLevel4 != null)
            taken.Add(build.ClassFeatLevel4.Value.Id);
        _pendingId = selected;
        var options = new List<Pf2eListEntry>();

        foreach (var feat in _prototypeManager.EnumeratePrototypes<CFFeatPrototype>()
                     .Where(feat => feat.FitsClass(classId, maxLevel) && CFClassChoices.MeetsRequirement(_prototypeManager, CurrentBuild(), feat) && !taken.Contains(feat.ID))
                     .OrderBy(feat => feat.Level)
                     .ThenBy(feat => Loc.GetString(feat.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new Pf2eListEntry
            {
                Id = feat.ID,
                Name = FeatName(feat),
                Rarity = feat.Rarity,
                Traits = Loc.GetString("cf-character-builder-feat-level", ("level", feat.Level)),
                Description = Loc.GetString(feat.Description),
            });
        }

        SelectionView.Show(
            Loc.GetString("cf-character-builder-title-class-feat"),
            Loc.GetString("cf-character-builder-filter-all-class-feats"),
            options,
            selected);
        OpenPicker();
    }

    /// <summary>A subclass option's name, marked if it has no effect in play yet.</summary>
    private static string ClassChoiceName(CFClassChoicePrototype choice)
    {
        var name = Loc.GetString(choice.Name);
        var unfinished = choice.Grants.Count == 0
                         && choice.Features.Count == 0
                         && choice.Choices.Count == 0
                         && choice.TrainedSkills.Count == 0
                         && choice.KeyAbility == null
                         && Loc.GetString(choice.Description).Contains("TBA");
        return unfinished ? Loc.GetString("cf-character-builder-feat-tba", ("name", name)) : name;
    }

    /// <summary>A heritage's name, marked if it has no effect in play yet.</summary>
    private static string HeritageName(CFHeritagePrototype heritage)
    {
        var name = Loc.GetString(heritage.Name);
        var unfinished = heritage.Grants.Count == 0
                         && heritage.TrainedSkills.Count == 0
                         && heritage.Choices.Count == 0
                         && heritage is { HitPoints: 0, Speed: 0, Size: null, Darkvision: false, UpgradeVision: false }
                         && Loc.GetString(heritage.Description).Contains("TBA");
        return unfinished ? Loc.GetString("cf-character-builder-feat-tba", ("name", name)) : name;
    }

    /// <summary>A feat's name, marked if it has no effect in play yet.</summary>
    private static string FeatName(CFFeatPrototype feat)
    {
        var name = Loc.GetString(feat.Name);
        var text = Loc.GetString(feat.Description);
        var unfinished = feat.PendingAction
                         || feat.Grants.Count == 0
                            && feat.Choices.Count == 0
                            && (text.Contains("implemented later")
                                || text.Contains("TBA")
                                || text.Contains("ot applied in play"));
        return unfinished ? Loc.GetString("cf-character-builder-feat-tba", ("name", name)) : name;
    }

    /// <summary>The list for a skill or general feat slot. Feats the build cannot qualify for by then are left out.</summary>
    private void ShowGeneralFeatSelection(BuilderSelection slot, int level)
    {
        if (Profile == null)
            return;

        _selection = slot;
        _prdUrl = AncestryPrd;
        AncestryButton.Pressed = false;
        BackgroundButton.Pressed = false;
        ClassButton.Pressed = false;
        JobButton.Pressed = false;
        HeritageSlot.Pressed = false;
        SpriteSlot.Pressed = false;
        AncestryFeatSlot.Pressed = false;
        ClassFeatSlot.Pressed = false;
        Level2FeatSlot.Pressed = false;
        Level4FeatSlot.Pressed = false;
        Level5FeatSlot.Pressed = false;

        var build = CurrentBuild();
        var ancestryId = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species)?.ID;
        var skillSlot = slot != BuilderSelection.GeneralFeatLevel3;
        var selected = slot switch
        {
            BuilderSelection.SkillFeatLevel2 => build.SkillFeatLevel2?.Id,
            BuilderSelection.GeneralFeatLevel3 => build.GeneralFeatLevel3?.Id,
            BuilderSelection.BonusSkillFeat => build.BonusSkillFeats.GetValueOrDefault(level),
            _ => build.SkillFeatLevel4?.Id,
        };

        if (slot == BuilderSelection.BonusSkillFeat)
            _bonusLevel = level;

        var taken = new HashSet<string?> { build.SkillFeatLevel2?.Id, build.GeneralFeatLevel3?.Id, build.SkillFeatLevel4?.Id };
        taken.UnionWith(build.BonusSkillFeats.Values);
        taken.UnionWith(CFSkills.BackgroundFeats(_prototypeManager, build).Select(id => id.Id));
        taken.Remove(selected);
        _pendingId = selected;

        var options = new List<Pf2eListEntry>();
        foreach (var feat in _prototypeManager.EnumeratePrototypes<CFFeatPrototype>()
                     .Where(feat => (skillSlot ? feat.FitsSkillSlot(level) : feat.FitsGeneralSlot(level))
                                    && !taken.Contains(feat.ID)
                                    && CFSkills.Qualifies(_prototypeManager, build, ancestryId, feat, level))
                     .OrderBy(feat => feat.Level)
                     .ThenBy(feat => Loc.GetString(feat.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            var description = Loc.GetString(feat.Description);
            if (feat.Prerequisites.Count > 0)
            {
                description += "\n" + Loc.GetString(
                    "cf-character-builder-feat-prerequisites",
                    ("prerequisites", string.Join("; ", feat.Prerequisites)));
            }

            options.Add(new Pf2eListEntry
            {
                Id = feat.ID,
                Name = FeatName(feat),
                Rarity = feat.Rarity,
                Traits = Loc.GetString("cf-character-builder-feat-level", ("level", feat.Level)),
                Description = description,
            });
        }

        SelectionView.Show(
            Loc.GetString(skillSlot ? "cf-character-builder-skill-feat" : "cf-character-builder-general-feat"),
            Loc.GetString("cf-character-builder-filter-all-feats"),
            options,
            selected);
        OpenPicker();
    }

    /// <summary>One skill to raise a rank, from those still below Expert before this level.</summary>
    private void ShowSkillIncrease(int level, bool bonus = false)
    {
        if (Profile == null)
            return;

        _bonusLevel = level;
        _skillMode = bonus
            ? SkillAssignMode.BonusIncrease
            : level == 3 ? SkillAssignMode.IncreaseLevel3 : SkillAssignMode.IncreaseLevel5;
        var build = CurrentBuild();
        var ancestryId = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species)?.ID;
        var allowed = new List<string>();
        var labels = new Dictionary<string, string>();
        foreach (var skill in _prototypeManager.EnumeratePrototypes<CFAbilitySkillPrototype>().OrderBy(skill => skill.Order))
        {
            if (CFSkills.BuildRank(_prototypeManager, build, ancestryId, skill.ID, level - 1) >= CFProficiencyRank.Expert)
                continue;

            allowed.Add(skill.ID);
            labels[skill.ID] = Loc.GetString(skill.Name);
        }

        var current = bonus
            ? build.BonusSkillIncreases.GetValueOrDefault(level)
            : level == 3 ? build.SkillIncreaseLevel3?.Id : build.SkillIncreaseLevel5?.Id;
        var slots = new List<SkillSlotDefinition>
        {
            new()
            {
                Id = "increase",
                Label = Loc.GetString("cf-character-builder-skill-increase"),
                Allowed = allowed,
                Labels = labels,
                Selected = current != null && allowed.Contains(current) ? current : null,
            },
        };

        SkillView.Show(
            Loc.GetString("cf-character-builder-skill-increase"),
            Loc.GetString("cf-character-builder-skill-increase-help"),
            slots);
        OpenSkillView();
    }

    /// <summary>Four different attributes. The boost view keeps a group's rows different.</summary>
    private void ShowLevelBoosts()
    {
        if (Profile == null)
            return;

        _levelBoosts = true;
        var build = CurrentBuild();
        var slots = new List<BoostSlotDefinition>();
        for (var i = 0; i < CFAttributeMath.LevelFiveBoostCount; i++)
        {
            slots.Add(new BoostSlotDefinition
            {
                Id = $"level-{i}",
                Group = "level",
                Label = Loc.GetString("cf-character-builder-level-boost-slot", ("index", i + 1)),
                Allowed = AllAbilities.ToList(),
                Selected = i < build.AbilityBoostsLevel5.Count ? build.AbilityBoostsLevel5[i] : null,
            });
        }

        BoostView.Show(
            Loc.GetString("cf-character-builder-level-boosts"),
            Loc.GetString("cf-character-builder-level-boosts-help"),
            slots);
        OpenBoostView();
    }

    private void ShowHeritageSelection()
    {
        if (Profile == null)
            return;

        _selection = BuilderSelection.Heritage;
        _prdUrl = AncestryPrd;
        AncestryButton.Pressed = false;
        BackgroundButton.Pressed = false;
        ClassButton.Pressed = false;
        JobButton.Pressed = false;
        HeritageSlot.Pressed = true;
        SpriteSlot.Pressed = false;
        AncestryFeatSlot.Pressed = false;
        ClassFeatSlot.Pressed = false;
        Level2FeatSlot.Pressed = false;
        Level4FeatSlot.Pressed = false;
        Level5FeatSlot.Pressed = false;

        var ancestry = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species);
        var selected = Profile.CFBuild.Heritage?.Id;
        _pendingId = selected;
        var options = new List<Pf2eListEntry>();

        foreach (var heritage in _prototypeManager.EnumeratePrototypes<CFHeritagePrototype>()
                     .Where(heritage => heritage.Fits(ancestry?.ID))
                     .OrderBy(heritage => Loc.GetString(heritage.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            var traits = heritage.Ancestries.Count == 0
                ? Loc.GetString("cf-character-builder-heritage-versatile")
                : Loc.GetString(ancestry?.Name ?? "cf-character-builder-none");
            options.Add(new Pf2eListEntry
            {
                Id = heritage.ID,
                Name = HeritageName(heritage),
                Rarity = heritage.Rarity,
                Traits = traits,
                Description = Loc.GetString(heritage.Description),
                HitPoints = heritage.HitPoints > 0 ? Stat("cf-character-builder-stat-hp", heritage.HitPoints.ToString()) : null,
                Size = heritage.Size is { } size
                    ? Stat("cf-character-builder-stat-size", Loc.GetString($"cf-size-{size.ToString().ToLower()}"))
                    : null,
                Speed = heritage.Speed > 0
                    ? Stat("cf-character-builder-stat-speed", $"{heritage.Speed} {Loc.GetString("cf-character-builder-feet")}")
                    : null,
            });
        }

        SelectionView.Show(
            Loc.GetString("cf-character-builder-title-heritage"),
            Loc.GetString("cf-character-builder-filter-all-heritages"),
            options,
            selected);
        OpenPicker();
    }

    /// <summary>One button per subclass slot the class has options in, and one for a deity if it follows one.</summary>
    private void RefreshClassChoiceSlots(CFCharacterBuildData build)
    {
        ClassChoiceSlots.RemoveAllChildren();
        DeitySlot.Visible = false;
        if (build.Class is not { } classId || !_prototypeManager.TryIndex(classId, out CFClassPrototype? cls))
            return;

        var none = Loc.GetString("cf-character-builder-none");
        var selected = CFClassChoices.Selected(_prototypeManager, build);
        foreach (var slot in CFClassChoices.Slots(_prototypeManager, classId))
        {
            var pick = selected.FirstOrDefault(choice => choice.Slot == slot);
            var button = new Button
            {
                Text = Loc.GetString(
                    "cf-character-builder-choice-selected",
                    ("slot", Loc.GetString(slot)),
                    ("name", pick == null ? none : Loc.GetString(pick.Name))),
                HorizontalExpand = true,
            };
            button.OnPressed += _ => ShowClassChoiceSelection(slot);
            ClassChoiceSlots.AddChild(button);
        }

        if (!cls.UsesDeity)
            return;

        DeitySlot.Visible = true;
        DeitySlot.Disabled = !_prototypeManager.EnumeratePrototypes<CFDeityPrototype>().Any();
        if (DeitySlot.Disabled)
        {
            DeitySlot.Text = Loc.GetString("cf-character-builder-deity-tba");
            return;
        }

        var deity = CFClassChoices.SelectedDeity(_prototypeManager, build);
        DeitySlot.Text = Loc.GetString(
            "cf-character-builder-deity-selected",
            ("name", deity == null ? none : Loc.GetString(deity.Name)));
    }

    private void ReleaseBuilderSlots()
    {
        AncestryButton.Pressed = false;
        BackgroundButton.Pressed = false;
        ClassButton.Pressed = false;
        JobButton.Pressed = false;
        HeritageSlot.Pressed = false;
        SpriteSlot.Pressed = false;
        AncestryFeatSlot.Pressed = false;
        ClassFeatSlot.Pressed = false;
        Level2FeatSlot.Pressed = false;
        Level4FeatSlot.Pressed = false;
        Level5FeatSlot.Pressed = false;
    }

    private void ShowClassChoiceSelection(LocId slot)
    {
        if (Profile?.CFBuild.Class is not { } classId)
            return;

        _selection = BuilderSelection.ClassChoice;
        _prdUrl = ClassPrd;
        ReleaseBuilderSlots();

        var selected = CFClassChoices.Selected(_prototypeManager, Profile.CFBuild)
            .FirstOrDefault(choice => choice.Slot == slot)?.ID;
        _pendingId = selected;

        var labels = SkillLabelMap();
        var options = new List<Pf2eListEntry>();
        foreach (var choice in _prototypeManager.EnumeratePrototypes<CFClassChoicePrototype>()
                     .Where(choice => choice.Class == classId && choice.Slot == slot)
                     .OrderBy(choice => Loc.GetString(choice.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new Pf2eListEntry
            {
                Id = choice.ID,
                Name = ClassChoiceName(choice),
                Rarity = choice.Rarity,
                Traits = choice.KeyAbility is { } key
                    ? Loc.GetString("cf-character-builder-class-key-ability", ("ability", AbilityName(key)))
                    : null,
                Description = Loc.GetString(choice.Description),
                Rules = DescribeTrainedSkills(choice.TrainedSkills, labels),
            });
        }

        SelectionView.Show(
            Loc.GetString(slot),
            Loc.GetString("cf-character-builder-filter-all-choices"),
            options,
            selected);
        OpenPicker();
    }

    private void ShowDeitySelection()
    {
        if (Profile == null)
            return;

        _selection = BuilderSelection.Deity;
        _prdUrl = ClassPrd;
        ReleaseBuilderSlots();

        var selected = CFClassChoices.SelectedDeity(_prototypeManager, Profile.CFBuild)?.ID;
        _pendingId = selected;

        var labels = SkillLabelMap();
        var options = new List<Pf2eListEntry>();
        foreach (var deity in _prototypeManager.EnumeratePrototypes<CFDeityPrototype>()
                     .OrderBy(deity => Loc.GetString(deity.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new Pf2eListEntry
            {
                Id = deity.ID,
                Name = Loc.GetString(deity.Name),
                Rarity = deity.Rarity,
                Description = Loc.GetString(deity.Description),
                Rules = DescribeTrainedSkills(deity.TrainedSkills, labels),
            });
        }

        SelectionView.Show(
            Loc.GetString("cf-character-builder-title-deity"),
            Loc.GetString("cf-character-builder-filter-all-deities"),
            options,
            selected);
        OpenPicker();
    }

    private static string? DescribeTrainedSkills(
        List<ProtoId<CFAbilitySkillPrototype>> skills,
        IReadOnlyDictionary<string, string> labels)
    {
        if (skills.Count == 0)
            return null;

        var names = skills.Select(skill => labels.GetValueOrDefault(skill.Id, skill.Id));
        return Loc.GetString("cf-character-builder-choice-trained", ("skills", string.Join(", ", names)));
    }

    private void ShowSpriteSelection()
    {
        if (Profile == null)
            return;

        _selection = BuilderSelection.Sprite;
        _prdUrl = AncestryPrd;
        AncestryButton.Pressed = false;
        BackgroundButton.Pressed = false;
        ClassButton.Pressed = false;
        JobButton.Pressed = false;
        HeritageSlot.Pressed = false;
        SpriteSlot.Pressed = true;
        AncestryFeatSlot.Pressed = false;
        ClassFeatSlot.Pressed = false;
        Level2FeatSlot.Pressed = false;
        Level4FeatSlot.Pressed = false;
        Level5FeatSlot.Pressed = false;

        var ancestry = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species);
        var selected = Profile.Species.Id;
        _pendingId = selected;
        var options = new List<Pf2eListEntry>();

        if (ancestry != null)
        {
            var defaultName = ancestry.Species.Id is "Human" or "Dwarf"
                ? Loc.GetString(_prototypeManager.Index<SpeciesPrototype>(ancestry.Species).Name)
                : Loc.GetString("cf-sprite-humanoid");
            options.Add(new Pf2eListEntry
            {
                Id = ancestry.Species,
                Name = defaultName,
                Rarity = CFRarity.Common,
                Traits = Loc.GetString(ancestry.Name),
                Description = WithSpriteCredits(Loc.GetString("cf-sprite-humanoid-desc"), "Mobs/Species/Human/parts.rsi"),
            });

            var heritageId = Profile.CFBuild.Heritage?.Id;
            foreach (var sprite in ancestry.Sprites.OrderBy(sprite => Loc.GetString(sprite.Name), StringComparer.CurrentCultureIgnoreCase))
            {
                if (sprite.Heritage is { } required && required.Id != heritageId)
                    continue;

                options.Add(new Pf2eListEntry
                {
                    Id = sprite.Species,
                    Name = Loc.GetString(sprite.Name),
                    Rarity = CFRarity.Common,
                    Traits = Loc.GetString(ancestry.Name),
                    Description = WithSpriteCredits(Loc.GetString(sprite.Description), sprite.Parts),
                });
            }
        }

        SelectionView.Show(
            Loc.GetString("cf-character-builder-title-sprite"),
            Loc.GetString("cf-character-builder-filter-all-sprites"),
            options,
            selected);
        OpenPicker();
    }

    /// <summary>Appends the parts RSI license and copyright so the picker shows who drew the sprite.</summary>
    private string WithSpriteCredits(string description, string? rsiPath)
    {
        var credits = ReadSpriteCredits(rsiPath);
        return string.IsNullOrEmpty(credits) ? description : $"{description}\n\n{credits}";
    }

    private string? ReadSpriteCredits(string? rsiPath)
    {
        if (string.IsNullOrWhiteSpace(rsiPath))
            return null;

        var path = new ResPath($"/Textures/{rsiPath}/meta.json");
        if (!_resManager.TryContentFileRead(path, out var stream))
            return null;

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            var text = reader.ReadToEnd();
            var license = MatchMeta(text, "license");
            var copyright = MatchMeta(text, "copyright");
            if (license == null && copyright == null)
                return null;

            return license != null && copyright != null ? $"{license}\n{copyright}" : license ?? copyright;
        }
    }

    /// <summary>A <c>"license"</c> or <c>"copyright"</c> string in an RSI's meta.json.</summary>
    private static readonly Regex MetaField = new("\"(?<key>license|copyright)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"");

    private static string? MatchMeta(string text, string key)
    {
        for (var match = MetaField.Match(text); match.Success; match = match.NextMatch())
        {
            if (match.Groups["key"].Value == key)
                return Regex.Unescape(match.Groups["value"].Value);
        }

        return null;
    }

    private void ShowBoostAssignment()
    {
        if (Profile == null)
            return;

        _levelBoosts = false;
        var build = CurrentBuild();
        var ancestry = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species);
        var slots = new List<BoostSlotDefinition>();

        if (ancestry != null)
        {
            var freeCount = CFAttributeMath.CountAncestryFreeBoosts(ancestry);
            var ancestryBlocked = new HashSet<CFAbility>();
            foreach (var boost in ancestry.AbilityBoosts)
            {
                if (boost != CFAbilityBoost.Free)
                    ancestryBlocked.Add((CFAbility) boost);
            }

            foreach (var flaw in ancestry.AbilityFlaws)
                ancestryBlocked.Add(flaw);

            var ancestryAllowed = AllAbilities.Where(ability => !ancestryBlocked.Contains(ability)).ToList();
            for (var i = 0; i < freeCount; i++)
            {
                CFAbility? selected = i < build.AncestryFreeBoosts.Count ? build.AncestryFreeBoosts[i] : null;
                slots.Add(new BoostSlotDefinition
                {
                    Id = $"ancestry-free-{i}",
                    Group = "ancestry",
                    Label = Loc.GetString("cf-character-builder-boost-ancestry-free", ("index", i + 1)),
                    Allowed = ancestryAllowed,
                    Selected = selected,
                });
            }
        }

        if (build.Background != null
            && _prototypeManager.TryIndex(build.Background.Value, out CFBackgroundPrototype? background))
        {
            var choices = CFAttributeMath.BackgroundBoostChoices(background);
            if (choices.Count > 0)
            {
                slots.Add(new BoostSlotDefinition
                {
                    Id = "background-choice",
                    Group = "background",
                    Label = Loc.GetString("cf-character-builder-boost-background-choice"),
                    Allowed = choices,
                    Selected = build.BackgroundBoost,
                });
            }

            slots.Add(new BoostSlotDefinition
            {
                Id = "background-free",
                Group = "background",
                Label = Loc.GetString("cf-character-builder-boost-background-free"),
                Allowed = AllAbilities.ToList(),
                Selected = build.BackgroundFreeBoost,
            });
        }

        for (var i = 0; i < CFAttributeMath.FinishingFreeBoostCount; i++)
        {
            CFAbility? selected = i < build.FreeBoosts.Count ? build.FreeBoosts[i] : null;
            slots.Add(new BoostSlotDefinition
            {
                Id = $"free-{i}",
                Group = "finishing",
                Label = Loc.GetString("cf-character-builder-boost-finishing", ("index", i + 1)),
                Allowed = AllAbilities.ToList(),
                Selected = selected,
            });
        }

        if (slots.Count == 0)
        {
            // Nothing to assign yet — still show empty guidance.
            BoostView.Show(
                Loc.GetString("cf-character-builder-set-abilities"),
                Loc.GetString("cf-character-builder-boost-need-abc"),
                Array.Empty<BoostSlotDefinition>());
            OpenBoostView();
            return;
        }

        BoostView.Show(
            Loc.GetString("cf-character-builder-set-abilities"),
            Loc.GetString("cf-character-builder-boost-help"),
            slots);
        OpenBoostView();
    }

    private void AcceptBoostAssignment()
    {
        if (Profile == null)
            return;

        var selections = BoostView.GetSelections();
        var build = CurrentBuild();
        if (_levelBoosts)
        {
            build.AbilityBoostsLevel5.Clear();
            for (var i = 0; i < CFAttributeMath.LevelFiveBoostCount; i++)
            {
                if (selections.TryGetValue($"level-{i}", out var boost) && boost is { } ability && !build.AbilityBoostsLevel5.Contains(ability))
                    build.AbilityBoostsLevel5.Add(ability);
            }

            ClosePicker();
            SetBuild(build);
            return;
        }

        var ancestry = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species);

        build.AncestryFreeBoosts.Clear();
        if (ancestry != null)
        {
            var freeCount = CFAttributeMath.CountAncestryFreeBoosts(ancestry);
            var picked = new HashSet<CFAbility>();
            for (var i = 0; i < freeCount; i++)
            {
                if (!selections.TryGetValue($"ancestry-free-{i}", out var ability) || ability == null)
                    continue;

                if (!picked.Add(ability.Value))
                    continue;

                build.AncestryFreeBoosts.Add(ability.Value);
            }
        }

        if (selections.TryGetValue("background-choice", out var bgChoice))
            build.BackgroundBoost = bgChoice;

        if (selections.TryGetValue("background-free", out var bgFree))
            build.BackgroundFreeBoost = bgFree;

        build.FreeBoosts.Clear();
        var finishingPicked = new HashSet<CFAbility>();
        for (var i = 0; i < CFAttributeMath.FinishingFreeBoostCount; i++)
        {
            if (!selections.TryGetValue($"free-{i}", out var ability) || ability == null)
                continue;

            if (!finishingPicked.Add(ability.Value))
                continue;

            build.FreeBoosts.Add(ability.Value);
        }

        // Int can change free class-skill slot count — trim excess picks.
        if (build.Class != null
            && _prototypeManager.TryIndex(build.Class.Value, out CFClassPrototype? cls))
        {
            var preview = Profile.WithCFBuild(build);
            var mods = CFAttributeMath.Snapshot(_prototypeManager, preview).Modifiers;
            var need = CFSkills.ClassSkillSlotCount(cls, mods);
            if (build.ClassTrainedSkills.Count > need)
                build.ClassTrainedSkills = build.ClassTrainedSkills.Take(need).ToList();
        }

        ClosePicker();
        SetBuild(build);
    }

    private void ShowSkillTraining()
    {
        _skillMode = SkillAssignMode.Background;
        var build = CurrentBuild();
        if (build.Background == null
            || !_prototypeManager.TryIndex(build.Background.Value, out CFBackgroundPrototype? background))
        {
            SkillView.Show(
                Loc.GetString("cf-character-builder-skill-training"),
                Loc.GetString("cf-character-builder-skill-need-background"),
                Array.Empty<SkillSlotDefinition>());
            OpenSkillView();
            return;
        }

        var defaults = CFSkills.BackgroundSkillSlots(background);
        if (defaults.Count == 0)
        {
            SkillView.Show(
                Loc.GetString("cf-character-builder-skill-training"),
                Loc.GetString("cf-character-builder-skill-need-background"),
                Array.Empty<SkillSlotDefinition>());
            OpenSkillView();
            return;
        }

        var labels = SkillLabelMap();
        var allowed = labels.Keys.OrderBy(id => labels[id], StringComparer.CurrentCultureIgnoreCase).ToList();
        var slots = new List<SkillSlotDefinition>();
        for (var i = 0; i < defaults.Count; i++)
        {
            string? selected = null;
            if (i < build.BackgroundTrainedSkills.Count)
                selected = build.BackgroundTrainedSkills[i].Id;
            else
                selected = defaults[i].Id;

            slots.Add(new SkillSlotDefinition
            {
                Id = $"bg-skill-{i}",
                Label = Loc.GetString("cf-character-builder-skill-slot", ("index", i + 1)),
                Allowed = allowed,
                Labels = labels,
                Selected = selected,
            });
        }

        SkillView.Show(
            Loc.GetString("cf-character-builder-skill-training"),
            Loc.GetString("cf-character-builder-skill-help"),
            slots);
        OpenSkillView();
    }

    private void ShowLanguages()
    {
        if (Profile == null)
            return;

        _skillMode = SkillAssignMode.Language;
        var build = CurrentBuild();
        var sheet = CFAttributeMath.Snapshot(_prototypeManager, Profile);
        var count = CFLanguageRules.PickCount(sheet.Modifiers);
        var title = Loc.GetString("cf-character-builder-languages");
        if (count == 0)
        {
            SkillView.Show(title, Loc.GetString("cf-character-builder-languages-none"), Array.Empty<SkillSlotDefinition>());
            OpenSkillView();
            return;
        }

        var pickable = CFLanguageRules.Pickable(_prototypeManager, sheet.Ancestry);
        var allowed = pickable.Select(language => language.ID).ToList();
        var labels = pickable.ToDictionary(language => language.ID, language => Loc.GetString(language.Name));
        var slots = new List<SkillSlotDefinition>();
        for (var i = 0; i < count; i++)
        {
            string? selected = null;
            if (i < build.Languages.Count && allowed.Contains(build.Languages[i].Id))
                selected = build.Languages[i].Id;

            slots.Add(new SkillSlotDefinition
            {
                Id = $"language-{i}",
                Label = Loc.GetString("cf-character-builder-language-slot", ("index", i + 1)),
                Allowed = allowed,
                Labels = labels,
                Selected = selected,
            });
        }

        SkillView.Show(title, Loc.GetString("cf-character-builder-languages-help"), slots);
        OpenSkillView();
    }

    private string FeatSlotText(string slot, ProtoId<CFFeatPrototype>? feat)
    {
        return feat != null && _prototypeManager.TryIndex(feat.Value, out var prototype)
            ? Loc.GetString("cf-character-builder-slot-selected", ("slot", Loc.GetString(slot)), ("name", Loc.GetString(prototype.Name)))
            : Loc.GetString(slot);
    }

    /// <summary>The extra skill feat and increase slots show only for a class that has them at that level.</summary>
    private void UpdateBonusSlots(CFCharacterBuildData build)
    {
        CFClassPrototype? cls = null;
        if (build.Class is { } classId)
            _prototypeManager.TryIndex(classId, out cls);

        foreach (var (button, level) in new[] { (Level1BonusSkillFeatSlot, 1), (Level3BonusSkillFeatSlot, 3), (Level5BonusSkillFeatSlot, 5) })
        {
            button.Visible = cls?.BonusSkillFeats.Contains(level) == true;
            button.Text = FeatSlotText(
                "cf-character-builder-skill-feat",
                build.BonusSkillFeats.TryGetValue(level, out var feat) ? new ProtoId<CFFeatPrototype>(feat) : (ProtoId<CFFeatPrototype>?) null);
        }

        foreach (var (button, level) in new[] { (Level2BonusSkillIncreaseSlot, 2), (Level4BonusSkillIncreaseSlot, 4) })
        {
            button.Visible = cls?.BonusSkillIncreases.Contains(level) == true;
            button.Text = SkillSlotText(
                build.BonusSkillIncreases.TryGetValue(level, out var skill) ? new ProtoId<CFAbilitySkillPrototype>(skill) : (ProtoId<CFAbilitySkillPrototype>?) null);
        }
    }

    private string SkillSlotText(ProtoId<CFAbilitySkillPrototype>? skill)
    {
        var slot = Loc.GetString("cf-character-builder-skill-increase");
        return skill != null && _prototypeManager.TryIndex(skill.Value, out var prototype)
            ? Loc.GetString("cf-character-builder-slot-selected", ("slot", slot), ("name", Loc.GetString(prototype.Name)))
            : slot;
    }

    private bool IsLanguagesComplete(CFCharacterBuildData build)
    {
        return Profile != null
               && build.Languages.Count >= CFLanguageRules.PickCount(CFAttributeMath.Snapshot(_prototypeManager, Profile).Modifiers);
    }

    private void ShowClassSkill()
    {
        _skillMode = SkillAssignMode.Class;
        var build = CurrentBuild();
        if (Profile == null
            || build.Class == null
            || !_prototypeManager.TryIndex(build.Class.Value, out CFClassPrototype? cls))
        {
            SkillView.Show(
                Loc.GetString("cf-character-builder-class-skill"),
                Loc.GetString("cf-character-builder-class-skill-need-class"),
                Array.Empty<SkillSlotDefinition>());
            OpenSkillView();
            return;
        }

        var sheet = CFAttributeMath.Snapshot(_prototypeManager, Profile);
        var slotCount = CFSkills.ClassSkillSlotCount(cls, sheet.Modifiers);
        if (slotCount == 0)
        {
            SkillView.Show(
                Loc.GetString("cf-character-builder-class-skill"),
                Loc.GetString("cf-character-builder-class-skill-need-class"),
                Array.Empty<SkillSlotDefinition>());
            OpenSkillView();
            return;
        }

        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in cls.Proficiencies.Skills.Trained)
            blocked.Add(skill);

        foreach (var skill in CFClassChoices.TrainedSkills(_prototypeManager, build))
            blocked.Add(skill);

        foreach (var skill in CFSkills.HeritageSkills(_prototypeManager, build, CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species)?.ID))
            blocked.Add(skill);

        if (build.BackgroundTrainedSkills.Count > 0)
        {
            foreach (var skill in build.BackgroundTrainedSkills)
                blocked.Add(skill.Id);
        }
        else if (build.Background != null
                 && _prototypeManager.TryIndex(build.Background.Value, out CFBackgroundPrototype? background))
        {
            foreach (var skill in CFSkills.BackgroundSkillSlots(background))
                blocked.Add(skill.Id);
        }

        var labels = SkillLabelMap();
        var allowed = labels.Keys
            .Where(id => !blocked.Contains(id))
            .OrderBy(id => labels[id], StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var slots = new List<SkillSlotDefinition>();
        for (var i = 0; i < slotCount; i++)
        {
            string? selected = i < build.ClassTrainedSkills.Count
                ? build.ClassTrainedSkills[i].Id
                : null;
            if (selected != null && !allowed.Contains(selected))
                selected = null;

            slots.Add(new SkillSlotDefinition
            {
                Id = $"class-skill-{i}",
                Label = Loc.GetString("cf-character-builder-class-skill-slot", ("index", i + 1)),
                Allowed = allowed,
                Labels = labels,
                Selected = selected,
            });
        }

        SkillView.Show(
            Loc.GetString("cf-character-builder-class-skill"),
            Loc.GetString("cf-character-builder-class-skill-help"),
            slots);
        OpenSkillView();
    }

    private void AcceptSkillAssignment()
    {
        if (Profile == null)
            return;

        var selections = SkillView.GetSelections();
        var build = CurrentBuild();

        if (_skillMode == SkillAssignMode.OptionChoice)
        {
            build.OptionPicks.RemoveAll(pick => pick.Source == _optionSource);
            foreach (var (slot, value) in selections)
            {
                if (value != null)
                    build.OptionPicks.Add(new CFOptionPick { Source = _optionSource, Slot = int.Parse(slot), Value = value });
            }

            build.ClassTrainedSkills.RemoveAll(skill => selections.Values.Contains(skill.Id));
            ClosePicker();
            SetBuild(build);
            return;
        }

        if (_skillMode == SkillAssignMode.BonusIncrease)
        {
            if (selections.TryGetValue("increase", out var bonusIncrease) && bonusIncrease != null)
                build.BonusSkillIncreases[_bonusLevel] = bonusIncrease;
            else
                build.BonusSkillIncreases.Remove(_bonusLevel);
        }
        else if (_skillMode is SkillAssignMode.IncreaseLevel3 or SkillAssignMode.IncreaseLevel5)
        {
            selections.TryGetValue("increase", out var increase);
            if (_skillMode == SkillAssignMode.IncreaseLevel3)
                build.SkillIncreaseLevel3 = increase;
            else
                build.SkillIncreaseLevel5 = increase;
        }
        else if (_skillMode == SkillAssignMode.Language)
        {
            build.Languages.Clear();
            foreach (var selected in selections.Values)
            {
                if (selected != null && build.Languages.All(language => language.Id != selected))
                    build.Languages.Add(selected);
            }
        }
        else if (_skillMode == SkillAssignMode.Background)
        {
            if (build.Background == null
                || !_prototypeManager.TryIndex(build.Background.Value, out CFBackgroundPrototype? background))
            {
                ClosePicker();
                return;
            }

            var need = CFSkills.BackgroundSkillSlots(background).Count;
            build.BackgroundTrainedSkills.Clear();
            var picked = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < need; i++)
            {
                if (!selections.TryGetValue($"bg-skill-{i}", out var skillId) || skillId == null)
                    continue;
                if (!picked.Add(skillId))
                    continue;
                build.BackgroundTrainedSkills.Add(skillId);
            }
        }
        else
        {
            if (build.Class == null
                || !_prototypeManager.TryIndex(build.Class.Value, out CFClassPrototype? cls))
            {
                ClosePicker();
                return;
            }

            var sheet = CFAttributeMath.Snapshot(_prototypeManager, Profile);
            var need = CFSkills.ClassSkillSlotCount(cls, sheet.Modifiers);
            build.ClassTrainedSkills.Clear();
            var picked = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < need; i++)
            {
                if (!selections.TryGetValue($"class-skill-{i}", out var skillId) || skillId == null)
                    continue;
                if (!picked.Add(skillId))
                    continue;
                build.ClassTrainedSkills.Add(skillId);
            }
        }

        ClosePicker();
        SetBuild(build);
    }

    private Dictionary<string, string> SkillLabelMap()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skill in _prototypeManager.EnumeratePrototypes<CFAbilitySkillPrototype>())
            labels[skill.ID] = Loc.GetString(skill.Name);
        return labels;
    }

    private void UpdateBuilderSummary()
    {
        UpdateAcceptButton();
        var ancestry = Loc.GetString("cf-character-builder-none");
        if (Profile != null && _prototypeManager.TryIndex(Profile.Species, out SpeciesPrototype? species))
            ancestry = Loc.GetString(species.Name);

        var build = CurrentBuild();

        var background = Loc.GetString("cf-character-builder-none");
        if (build.Background != null && _prototypeManager.TryIndex(build.Background.Value, out CFBackgroundPrototype? backgroundProto))
            background = Loc.GetString(backgroundProto.Name);

        var clsName = Loc.GetString("cf-character-builder-none");
        if (build.Class != null && _prototypeManager.TryIndex(build.Class.Value, out CFClassPrototype? classProto))
            clsName = Loc.GetString(classProto.Name);

        var jobName = Loc.GetString("cf-character-builder-none");
        if (Profile != null)
        {
            if (CFCharacterRequirements.ChosenJob(_prototypeManager, Profile) is { } chosenJob && _prototypeManager.TryIndex(chosenJob, out var job))
                jobName = job.LocalizedName;
        }

        AncestryButton.Text = $"{Loc.GetString("cf-character-builder-ancestry")}: {ancestry}";
        BackgroundButton.Text = $"{Loc.GetString("cf-character-builder-background")}: {background}";
        ClassButton.Text = $"{Loc.GetString("cf-character-builder-class")}: {clsName}";
        JobButton.Text = $"{Loc.GetString("cf-character-builder-job")}: {jobName}";

        var heritageName = Loc.GetString("cf-character-builder-none");
        if (build.Heritage != null && _prototypeManager.TryIndex(build.Heritage.Value, out CFHeritagePrototype? chosenHeritage))
            heritageName = Loc.GetString(chosenHeritage.Name);
        HeritageSlot.Text = Loc.GetString("cf-character-builder-heritage-selected", ("name", heritageName));
        RefreshClassChoiceSlots(build);
        RefreshOptionChoiceSlots(build);

        var featName = Loc.GetString("cf-character-builder-none");
        if (build.AncestryFeat != null && _prototypeManager.TryIndex(build.AncestryFeat.Value, out CFFeatPrototype? chosenFeat))
            featName = Loc.GetString(chosenFeat.Name);
        AncestryFeatSlot.Text = Loc.GetString("cf-character-builder-ancestry-feat-selected", ("name", featName));

        var classFeatName = Loc.GetString("cf-character-builder-none");
        if (build.ClassFeat != null && _prototypeManager.TryIndex(build.ClassFeat.Value, out CFFeatPrototype? chosenClassFeat))
            classFeatName = Loc.GetString(chosenClassFeat.Name);
        ClassFeatSlot.Text = Loc.GetString("cf-character-builder-class-feat-selected", ("name", classFeatName));

        var classFeatLevel2Name = Loc.GetString("cf-character-builder-none");
        if (build.ClassFeatLevel2 != null && _prototypeManager.TryIndex(build.ClassFeatLevel2.Value, out CFFeatPrototype? chosenClassFeatLevel2))
            classFeatLevel2Name = Loc.GetString(chosenClassFeatLevel2.Name);
        Level2FeatSlot.Text = Loc.GetString("cf-character-builder-class-feat-level-2-selected", ("name", classFeatLevel2Name));

        var classFeatLevel4Name = Loc.GetString("cf-character-builder-none");
        if (build.ClassFeatLevel4 != null && _prototypeManager.TryIndex(build.ClassFeatLevel4.Value, out CFFeatPrototype? chosenClassFeatLevel4))
            classFeatLevel4Name = Loc.GetString(chosenClassFeatLevel4.Name);
        Level4FeatSlot.Text = Loc.GetString("cf-character-builder-class-feat-level-4-selected", ("name", classFeatLevel4Name));

        var ancestryFeatLevel5Name = Loc.GetString("cf-character-builder-none");
        if (build.AncestryFeatLevel5 != null && _prototypeManager.TryIndex(build.AncestryFeatLevel5.Value, out CFFeatPrototype? chosenAncestryFeatLevel5))
            ancestryFeatLevel5Name = Loc.GetString(chosenAncestryFeatLevel5.Name);
        Level5FeatSlot.Text = Loc.GetString("cf-character-builder-ancestry-feat-level-5-selected", ("name", ancestryFeatLevel5Name));

        var spriteName = Loc.GetString("cf-sprite-humanoid");
        if (Profile != null)
        {
            var spriteAncestry = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species);
            var spriteMatch = spriteAncestry?.Sprites.FirstOrDefault(sprite => sprite.Species == Profile.Species);
            if (spriteMatch != null)
                spriteName = Loc.GetString(spriteMatch.Name);
            else if (Profile.Species.Id is "Human" or "Dwarf")
                spriteName = ancestry;
        }
        SpriteSlot.Text = Loc.GetString("cf-character-builder-sprite-selected", ("name", spriteName));

        var boostComplete = IsBoostAssignmentComplete(build);
        SetAbilitiesButton.Text = boostComplete
            ? Loc.GetString("cf-character-builder-set-abilities-done")
            : Loc.GetString("cf-character-builder-set-abilities");

        SkillTrainingButton.Text = IsSkillTrainingComplete(build)
            ? Loc.GetString("cf-character-builder-skill-training-done")
            : Loc.GetString("cf-character-builder-skill-training");

        ClassSkillButton.Text = IsClassSkillComplete(build)
            ? Loc.GetString("cf-character-builder-class-skill-done")
            : Loc.GetString("cf-character-builder-class-skill");

        Level2SkillFeatSlot.Text = FeatSlotText("cf-character-builder-skill-feat", build.SkillFeatLevel2);
        Level3GeneralFeatSlot.Text = FeatSlotText("cf-character-builder-general-feat", build.GeneralFeatLevel3);
        Level4SkillFeatSlot.Text = FeatSlotText("cf-character-builder-skill-feat", build.SkillFeatLevel4);
        Level3SkillIncreaseSlot.Text = SkillSlotText(build.SkillIncreaseLevel3);
        Level5SkillIncreaseSlot.Text = SkillSlotText(build.SkillIncreaseLevel5);
        UpdateBonusSlots(build);
        Level5BoostsSlot.Text = build.AbilityBoostsLevel5.Count >= CFAttributeMath.LevelFiveBoostCount
            ? Loc.GetString("cf-character-builder-level-boosts-done")
            : Loc.GetString("cf-character-builder-level-boosts");

        LanguagesButton.Text = IsLanguagesComplete(build)
            ? Loc.GetString("cf-character-builder-languages-done")
            : Loc.GetString("cf-character-builder-languages");

        UpdateHeaderStats();
    }

    private bool IsSkillTrainingComplete(CFCharacterBuildData build)
    {
        if (build.Background == null
            || !_prototypeManager.TryIndex(build.Background.Value, out CFBackgroundPrototype? background))
            return false;

        var need = CFSkills.BackgroundSkillSlots(background).Count;
        return need > 0 && build.BackgroundTrainedSkills.Count >= need;
    }

    private bool IsClassSkillComplete(CFCharacterBuildData build)
    {
        if (Profile == null
            || build.Class == null
            || !_prototypeManager.TryIndex(build.Class.Value, out CFClassPrototype? cls))
            return false;

        var sheet = CFAttributeMath.Snapshot(_prototypeManager, Profile);
        var need = CFSkills.ClassSkillSlotCount(cls, sheet.Modifiers);
        return need > 0 && build.ClassTrainedSkills.Count >= need;
    }

    private bool IsBoostAssignmentComplete(CFCharacterBuildData build)
    {
        if (Profile == null)
            return false;

        var ancestry = CFAttributeMath.FindAncestry(_prototypeManager, Profile.Species);
        if (ancestry != null)
        {
            var need = CFAttributeMath.CountAncestryFreeBoosts(ancestry);
            if (build.AncestryFreeBoosts.Count < need)
                return false;
        }

        if (build.Background != null)
        {
            if (build.BackgroundFreeBoost == null)
                return false;

            if (_prototypeManager.TryIndex(build.Background.Value, out CFBackgroundPrototype? background)
                && CFAttributeMath.BackgroundBoostChoices(background).Count > 0
                && build.BackgroundBoost == null)
            {
                return false;
            }
        }

        return build.FreeBoosts.Count >= CFAttributeMath.FinishingFreeBoostCount;
    }

    private void UpdateHeaderStats()
    {
        if (Profile == null)
            return;

        var sheet = CFAttributeMath.Snapshot(_prototypeManager, Profile);
        var mods = sheet.Modifiers;
        var ancestry = sheet.Ancestry;

        CFHeritagePrototype? heritage = null;
        if (Profile.CFBuild.Heritage != null)
            _prototypeManager.TryIndex(Profile.CFBuild.Heritage.Value, out heritage);
        if (heritage != null && !heritage.Fits(ancestry?.ID))
            heritage = null;

        var size = ancestry == null && heritage?.Size == null
            ? Loc.GetString("cf-character-builder-none")
            : Loc.GetString($"cf-size-{CFAttributeMath.EffectiveSize(ancestry, heritage).ToString().ToLower()}");
        var speedFeet = CFAttributeMath.EffectiveSpeed(ancestry, heritage);
        var speed = ancestry == null && heritage == null
            ? Loc.GetString("cf-character-builder-none")
            : $"{speedFeet} {Loc.GetString("cf-character-builder-feet")}";

        SizeLabel.Text = Loc.GetString("cf-character-builder-size", ("size", size));
        SpeedLabel.Text = Loc.GetString("cf-character-builder-speed", ("speed", speed));
        StrLabel.Text = Loc.GetString("cf-character-builder-str", ("score", CFAbilityScores.FormatModifier(mods[CFAbility.Strength])));
        DexLabel.Text = Loc.GetString("cf-character-builder-dex", ("score", CFAbilityScores.FormatModifier(mods[CFAbility.Dexterity])));
        ConLabel.Text = Loc.GetString("cf-character-builder-con", ("score", CFAbilityScores.FormatModifier(mods[CFAbility.Constitution])));
        IntLabel.Text = Loc.GetString("cf-character-builder-int", ("score", CFAbilityScores.FormatModifier(mods[CFAbility.Intelligence])));
        WisLabel.Text = Loc.GetString("cf-character-builder-wis", ("score", CFAbilityScores.FormatModifier(mods[CFAbility.Wisdom])));
        ChaLabel.Text = Loc.GetString("cf-character-builder-cha", ("score", CFAbilityScores.FormatModifier(mods[CFAbility.Charisma])));

        HpSummary.Text = Loc.GetString("cf-character-builder-hp-summary", ("current", sheet.MaxHp), ("max", sheet.MaxHp));
        AcValue.Text = sheet.ArmorClass.ToString();

        FortitudeLabel.Text = SaveLine("cf-character-builder-save-fortitude",
            CFAttributeMath.IsSaveTrained(sheet.Class, "Fortitude"),
            sheet.Fortitude);
        ReflexLabel.Text = SaveLine("cf-character-builder-save-reflex",
            CFAttributeMath.IsSaveTrained(sheet.Class, "Reflex"),
            sheet.Reflex);
        WillLabel.Text = SaveLine("cf-character-builder-save-will",
            CFAttributeMath.IsSaveTrained(sheet.Class, "Will"),
            sheet.Will);

        SpellsTab.Rebuild(Profile, _prototypeManager, SetBuild);
        ApplyPetTab();
    }

    private void ApplyPetTab()
    {
        var show = PetTab.Rebuild(Profile, _prototypeManager, SetBuild);
        TabContainer.SetTabVisible(PetTab, show);
        if (!show && TabContainer.CurrentTab == PetTabIndex)
            TabContainer.CurrentTab = DetailsTab;
    }

    private static string SaveLine(string saveLoc, bool trained, int bonus)
    {
        var rank = Loc.GetString(trained
            ? "cf-character-builder-prof-trained-short"
            : "cf-character-builder-prof-untrained-short");
        return Loc.GetString("cf-character-builder-save-line",
            ("rank", rank),
            ("bonus", CFAbilityScores.FormatModifier(bonus)),
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
            WeaponsTab.Apply(updated);
            DefenseTab.Apply(updated);
            GearTab.Apply(updated);
            ShopTab.Apply(updated);
        }

        WeaponsTab.Rebuild(Profile, JobOverride, session, collection, _prototypeManager, GetProfile, OnChanged);
        DefenseTab.Rebuild(Profile, JobOverride, session, collection, _prototypeManager, GetProfile, OnChanged);
        GearTab.Rebuild(Profile, JobOverride, session, collection, _prototypeManager, GetProfile, OnChanged);
        ShopTab.Rebuild(Profile, JobOverride, session, collection, _prototypeManager, GetProfile, OnChanged);
        SpellsTab.Rebuild(Profile, _prototypeManager, SetBuild);
        ApplyPetTab();
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

    private string JoinLanguages(IEnumerable<ProtoId<CFLanguagePrototype>> ids)
    {
        return JoinLoc(ids.Select(id => _prototypeManager.Index(id).Name));
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
        AddRank(lines, "cf-character-builder-trained", label, list.Trained);
        AddRank(lines, "cf-character-builder-expert", label, list.Expert);
        AddRank(lines, "cf-character-builder-master", label, list.Master);
        AddRank(lines, "cf-character-builder-legendary", label, list.Legendary);
    }

    private static void AddRank(List<string> lines, string loc, string label, List<string> items)
    {
        if (items.Count == 0)
            return;

        lines.Add(Loc.GetString(loc, ("items", $"{label}: {string.Join(", ", items)}")));
    }
}
