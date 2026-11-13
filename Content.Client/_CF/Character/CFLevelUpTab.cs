using System.Linq;
using Content.Client.CF.Lobby.UI.CharacterBuilder;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Languages;
using Content.Shared.CF.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client.CF.Character;

/// <summary>
/// The character sheet's Level Up tab. Once a character has the XP, the player picks the next level here: feats, skill
/// increases, boosts, new spell slots and the choices those bring. Accept sends them, and the server checks them as it
/// does a lobby build. The lobby only builds level 1.
/// </summary>
public sealed partial class CFLevelUpTab : BoxContainer
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;

    private readonly Label _status;
    private readonly BoxContainer _body;
    private readonly Label _missing;
    private readonly Button _accept;

    private EntityUid? _entity;
    private int _level;
    private bool _ready;
    private CFCharacterBuildData? _source;
    private CFCharacterBuildData? _build;
    private string? _ancestryId;
    private Dictionary<CFAbility, int> _modifiers = new();
    private List<ProtoId<CFLanguagePrototype>> _known = new();

    /// <summary>The language this level added to the build, so changing it replaces it.</summary>
    private ProtoId<CFLanguagePrototype>? _addedLanguage;

    public CFLevelUpTab()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);
        SeparationOverride = 6;

        _status = new Label { StyleClasses = { "LabelHeading" } };
        AddChild(_status);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _body = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        scroll.AddChild(_body);
        AddChild(scroll);

        var footer = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center,
            SeparationOverride = 12,
        };
        _missing = new Label { VerticalAlignment = VAlignment.Center };
        _accept = new Button { Text = Loc.GetString("cf-levelup-accept"), MinSize = new System.Numerics.Vector2(160, 32) };
        _accept.OnPressed += _ => Accept();
        footer.AddChild(_missing);
        footer.AddChild(_accept);
        AddChild(footer);
    }

    /// <summary>Called whenever the sheet refreshes. Starts over when the level, the XP or the stored build changes.</summary>
    public void Update(EntityUid entity)
    {
        if (!_entities.TryGetComponent(entity, out CFExperienceComponent? xp)
            || !_entities.TryGetComponent(entity, out CFCombatantComponent? combat))
        {
            _status.Text = Loc.GetString("cf-levelup-none");
            _body.RemoveAllChildren();
            _accept.Visible = false;
            _missing.Visible = false;
            return;
        }

        var ready = xp.CanLevelUp(combat.Level) && xp.Build != null;
        if (entity == _entity && combat.Level == _level && ready == _ready && Equals(xp.Build, _source))
            return;

        _entity = entity;
        _level = combat.Level;
        _ready = ready;
        _source = xp.Build;
        _build = xp.Build?.Clone();
        _ancestryId = xp.AncestryId;
        _addedLanguage = null;
        _modifiers = _entities.TryGetComponent(entity, out CFAbilityScoreComponent? scores)
            ? new Dictionary<CFAbility, int>(scores.AbilityModifiers)
            : new Dictionary<CFAbility, int>();
        _known = _entities.TryGetComponent(entity, out CFLanguagesComponent? languages)
            ? new List<ProtoId<CFLanguagePrototype>>(languages.Known)
            : new List<ProtoId<CFLanguagePrototype>>();

        if (!ready)
        {
            _status.Text = combat.Level >= CFExperienceComponent.MaxLevel
                ? Loc.GetString("cf-levelup-max", ("level", combat.Level))
                : Loc.GetString("cf-levelup-need-xp",
                    ("xp", CFExperienceComponent.XpPerLevel - xp.Xp),
                    ("level", combat.Level + 1));
            _body.RemoveAllChildren();
            _accept.Visible = false;
            _missing.Visible = false;
            return;
        }

        Rebuild();
    }

    private int Next => _level + 1;

    private void Rebuild()
    {
        _body.RemoveAllChildren();
        if (_build == null)
            return;

        _status.Text = Loc.GetString("cf-levelup-title", ("level", Next));
        _accept.Visible = true;
        _missing.Visible = true;

        foreach (var slot in CFLevelUp.SlotsAt(_prototypes, _build, Next))
        {
            switch (slot)
            {
                case CFLevelSlot.Boosts:
                    _body.AddChild(BoostsRow());
                    break;
                case CFLevelSlot.SkillIncrease:
                case CFLevelSlot.BonusSkillIncrease:
                    _body.AddChild(SkillRow(slot));
                    break;
                default:
                    _body.AddChild(FeatRow(slot));
                    break;
            }
        }

        if (CFLevelUp.NeedsLanguage(_build, Next, _modifiers))
            _body.AddChild(LanguageRow());

        var spells = CFLevelUp.NewSpellSlots(_prototypes, _build, Next);
        if (spells.Count > 0)
        {
            _body.AddChild(Heading(Loc.GetString("cf-levelup-spells")));
            foreach (var spell in spells)
            {
                _body.AddChild(SpellRow(spell));
            }
        }

        foreach (var option in CFLevelUp.NewOptionSlots(_prototypes, _build, _ancestryId, Next))
        {
            _body.AddChild(OptionRow(option));
        }

        var granted = CFLevelUp.GrantedSpells(_prototypes, _build, _ancestryId, Next);
        if (granted.Count > 0)
        {
            _body.AddChild(Heading(Loc.GetString("cf-spell-granted-title")));
            foreach (var spell in granted)
            {
                var name = _prototypes.TryIndex<CFSpellPrototype>(spell.Spell, out var proto) ? proto.Name : spell.Spell;
                _body.AddChild(new Label
                {
                    Text = Loc.GetString("cf-spell-granted-line", ("spell", name), ("source", Loc.GetString(spell.Source))),
                });
            }
        }

        RefreshMissing();
    }

    private void RefreshMissing()
    {
        if (_build == null)
            return;

        // Picks left open do not block Accept: they are skipped, and the server writes a report of them.
        var missing = CFLevelUp.Unfilled(_prototypes, _build, _ancestryId, Next, _modifiers, _known);
        _missing.Text = missing > 0
            ? Loc.GetString("cf-levelup-missing", ("count", missing))
            : Loc.GetString("cf-levelup-ready");
        _accept.Disabled = false;
    }

    private void Accept()
    {
        if (_build == null || !_ready)
            return;

        _entities.RaisePredictiveEvent(new CFLevelUpRequestEvent(_build));
        _accept.Disabled = true;
    }

    /// <summary>A change to the picks. Later rows can depend on earlier ones, so everything is drawn again.</summary>
    private void Changed()
    {
        if (_build == null)
            return;

        CFSkills.DropUnqualifiedFeats(_prototypes, _build, _ancestryId);
        Rebuild();
    }

    private static Label Heading(string text)
    {
        return new Label { Text = text, FontColorOverride = Color.FromHex("#E6C15A"), Margin = new Thickness(0, 6, 0, 0) };
    }

    private static BoxContainer Row(string label, Control control)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8, HorizontalExpand = true };
        row.AddChild(new Label { Text = label, MinWidth = 220, VerticalAlignment = VAlignment.Center });
        control.HorizontalExpand = true;
        row.AddChild(control);
        return row;
    }

    private static string SlotName(CFLevelSlot slot)
    {
        return Loc.GetString(slot switch
        {
            CFLevelSlot.ClassFeat => "cf-levelup-class-feat",
            CFLevelSlot.AncestryFeat => "cf-levelup-ancestry-feat",
            CFLevelSlot.SkillFeat => "cf-levelup-skill-feat",
            CFLevelSlot.GeneralFeat => "cf-levelup-general-feat",
            CFLevelSlot.BonusSkillFeat => "cf-levelup-bonus-skill-feat",
            CFLevelSlot.SkillIncrease => "cf-levelup-skill-increase",
            CFLevelSlot.BonusSkillIncrease => "cf-levelup-bonus-skill-increase",
            CFLevelSlot.Boosts => "cf-levelup-boosts",
            _ => "cf-levelup-language",
        });
    }

    private Control FeatRow(CFLevelSlot slot)
    {
        var current = CFLevelUp.Pick(_build!, slot, Next);
        var text = current != null && _prototypes.TryIndex<CFFeatPrototype>(current, out var feat)
            ? FeatName(feat)
            : Loc.GetString("cf-levelup-choose");
        var button = new Button { Text = text, ClipText = true };
        button.OnPressed += _ => OpenFeatPicker(slot);
        return Row(SlotName(slot), button);
    }

    private void OpenFeatPicker(CFLevelSlot slot)
    {
        if (_build == null)
            return;

        var entries = new List<Pf2eListEntry>();
        foreach (var feat in CFLevelUp.FeatOptions(_prototypes, _build, _ancestryId, slot, Next))
        {
            entries.Add(new Pf2eListEntry
            {
                Id = feat.ID,
                Name = FeatName(feat),
                Rarity = feat.Rarity,
                Traits = Loc.GetString("cf-character-builder-feat-level", ("level", feat.Level)),
                Description = Loc.GetString(feat.Description),
            });
        }

        var window = new DefaultWindow { Title = SlotName(slot), MinSize = new System.Numerics.Vector2(820, 560) };
        var view = new Pf2eSelectionView();
        string? pending = CFLevelUp.Pick(_build, slot, Next);
        view.OnEntrySelected += id => pending = id;
        view.OnClosed += window.Close;
        view.OnAccept += () =>
        {
            if (_build != null && pending != null)
            {
                CFLevelUp.SetPick(_build, slot, Next, pending);
                Changed();
            }

            window.Close();
        };
        window.Contents.AddChild(view);
        view.Show(SlotName(slot), Loc.GetString("cf-character-builder-filter-all-feats"), entries, pending);
        window.OpenCentered();
    }

    private Control SkillRow(CFLevelSlot slot)
    {
        var options = CFLevelUp.SkillOptions(_prototypes, _build!, _ancestryId, Next);
        var current = CFLevelUp.Pick(_build!, slot, Next);
        var button = new OptionButton();
        button.AddItem(Loc.GetString("cf-levelup-choose"), 0);
        for (var i = 0; i < options.Count; i++)
        {
            button.AddItem(Loc.GetString(options[i].Name), i + 1);
            if (options[i].ID == current)
                button.SelectId(i + 1);
        }

        button.OnItemSelected += args =>
        {
            button.SelectId(args.Id);
            CFLevelUp.SetPick(_build!, slot, Next, args.Id == 0 ? null : options[args.Id - 1].ID);
            Changed();
        };
        return Row(SlotName(slot), button);
    }

    private Control BoostsRow()
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        foreach (var ability in Enum.GetValues<CFAbility>())
        {
            var picked = _build!.AbilityBoostsLevel5.Contains(ability);
            var button = new Button
            {
                Text = Loc.GetString($"cf-ability-short-{ability.ToString().ToLowerInvariant()}"),
                ToggleMode = true,
                Pressed = picked,
                Disabled = !picked && _build.AbilityBoostsLevel5.Count >= CFAttributeMath.LevelFiveBoostCount,
            };
            var captured = ability;
            button.OnToggled += args =>
            {
                if (args.Pressed)
                    _build.AbilityBoostsLevel5.Add(captured);
                else
                    _build.AbilityBoostsLevel5.Remove(captured);
                Changed();
            };
            box.AddChild(button);
        }

        return Row(Loc.GetString("cf-levelup-boosts-pick", ("count", CFAttributeMath.LevelFiveBoostCount)), box);
    }

    private Control LanguageRow()
    {
        var options = CFLevelUp.LanguageOptions(_prototypes, _build!, _ancestryId, _modifiers, _known);
        var current = CFLevelUp.LevelFiveLanguage(_prototypes, _build!, _ancestryId, _modifiers);
        var button = new OptionButton();
        button.AddItem(Loc.GetString("cf-levelup-choose"), 0);
        for (var i = 0; i < options.Count; i++)
        {
            button.AddItem(Loc.GetString(options[i].Name), i + 1);
            if (options[i].ID == current)
                button.SelectId(i + 1);
        }

        button.OnItemSelected += args =>
        {
            button.SelectId(args.Id);
            if (_addedLanguage is { } old)
                _build!.Languages.Remove(old);
            _addedLanguage = null;
            if (args.Id > 0)
            {
                var language = new ProtoId<CFLanguagePrototype>(options[args.Id - 1].ID);
                _addedLanguage = language;
                _build!.Languages.Add(language);
            }

            Changed();
        };
        return Row(Loc.GetString("cf-levelup-language"), button);
    }

    private Control SpellRow(CFNewSpellSlot slot)
    {
        var pick = _build!.Spells.FirstOrDefault(p => p.Rank == slot.Rank && p.Index == slot.Index);
        var name = pick != null && pick.HasSpell && _prototypes.TryIndex(pick.Spell, out var spell)
            ? spell.Name
            : Loc.GetString("cf-levelup-choose");
        var button = new Button { Text = name, ClipText = true };
        button.OnPressed += _ => OpenSpellPicker(slot);
        var label = slot.Rank == 0
            ? Loc.GetString("cf-spell-rank-cantrips")
            : Loc.GetString("cf-spell-rank", ("rank", slot.Rank));
        return Row(label, button);
    }

    private void OpenSpellPicker(CFNewSpellSlot slot)
    {
        if (_build == null)
            return;

        var tradition = CFSpellTradition.Arcane;
        if (_build.Class is { } classId
            && _prototypes.TryIndex(classId, out CFClassPrototype? cls)
            && cls.Spellcasting != null)
        {
            tradition = CFClassChoices.Tradition(_prototypes, _build, cls.Spellcasting.Tradition);
        }

        var pick = _build.Spells.FirstOrDefault(p => p.Rank == slot.Rank && p.Index == slot.Index);
        var popup = new SpellSelectionPopup(slot.Rank, tradition, _prototypes, pick is { HasSpell: true } ? pick.Spell.Id : null);
        popup.OnSpellAccepted += id =>
        {
            _build?.Spells.RemoveAll(p => p.Rank == slot.Rank && p.Index == slot.Index);
            _build?.Spells.Add(new CFSpellPick { Rank = slot.Rank, Index = slot.Index, Spell = id });
            Changed();
        };
        popup.OnSpellCleared += () =>
        {
            _build?.Spells.RemoveAll(p => p.Rank == slot.Rank && p.Index == slot.Index);
            Changed();
        };
        popup.OpenCentered();
    }

    private Control OptionRow(CFOptionSlot slot)
    {
        var allowed = CFOptionChoices.Allowed(_prototypes, slot.Choice, _build);
        var current = _build!.OptionPicks.FirstOrDefault(p => p.Source == slot.Source && p.Slot == slot.Slot)?.Value;
        var button = new OptionButton();
        button.AddItem(Loc.GetString("cf-levelup-choose"), 0);
        for (var i = 0; i < allowed.Count; i++)
        {
            button.AddItem(OptionLabel(allowed[i]), i + 1);
            if (allowed[i] == current)
                button.SelectId(i + 1);
        }

        button.OnItemSelected += args =>
        {
            button.SelectId(args.Id);
            _build.OptionPicks.RemoveAll(p => p.Source == slot.Source && p.Slot == slot.Slot);
            if (args.Id > 0)
                _build.OptionPicks.Add(new CFOptionPick { Source = slot.Source, Slot = slot.Slot, Value = allowed[args.Id - 1] });
            Changed();
        };
        return Row(Loc.GetString(slot.SourceName), button);
    }

    private string OptionLabel(string value)
    {
        if (value.StartsWith(CFOptionChoices.SignaturePrefix, StringComparison.Ordinal))
            value = value[CFOptionChoices.SignaturePrefix.Length..];

        if (_prototypes.TryIndex<CFSpellPrototype>(value, out var spell))
            return spell.Name;

        if (_prototypes.TryIndex<CFLanguagePrototype>(value, out var language))
            return Loc.GetString(language.Name);

        if (_prototypes.TryIndex<CFAbilitySkillPrototype>(value, out var skill))
            return Loc.GetString(skill.Name);

        return value;
    }

    /// <summary>A feat's name, marked if it has no effect in play yet, as the lobby marks it.</summary>
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
}
