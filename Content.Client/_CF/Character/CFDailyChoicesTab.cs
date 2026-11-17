using System.Linq;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.CF.Character;

/// <summary>
/// The character sheet's Daily tab: the picks that can change each day, such as Flexible Studies' skill. Each row shows what
/// the character has today and lets the player choose what it becomes at the next long rest. Every change is sent to the
/// server at once, which checks it and keeps it as the plan.
/// </summary>
public sealed partial class CFDailyChoicesTab : BoxContainer
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;

    private readonly Label _status;
    private readonly BoxContainer _body;

    private EntityUid? _entity;
    private int _level;
    private CFCharacterBuildData? _source;
    private List<CFOptionPick> _plan = new();
    private List<CFOptionPick> _sourcePlan = new();

    public CFDailyChoicesTab()
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
    }

    /// <summary>The daily picks this character has. The window hides the tab when there are none.</summary>
    public static List<CFOptionSlot> SlotsOf(IEntityManager entities, IPrototypeManager prototypes, EntityUid entity)
    {
        if (!entities.TryGetComponent(entity, out CFExperienceComponent? xp)
            || xp.Build == null
            || !entities.TryGetComponent(entity, out CFCombatantComponent? combat))
        {
            return new List<CFOptionSlot>();
        }

        return CFOptionChoices.DailySlots(prototypes, xp.Build, xp.AncestryId, combat.Level);
    }

    /// <summary>Called whenever the sheet refreshes. Rebuilds when the level, the build or the stored plan changes.</summary>
    public void Update(EntityUid entity)
    {
        if (!_entities.TryGetComponent(entity, out CFExperienceComponent? xp)
            || xp.Build == null
            || !_entities.TryGetComponent(entity, out CFCombatantComponent? combat))
        {
            _status.Text = Loc.GetString("cf-daily-none");
            _body.RemoveAllChildren();
            _entity = null;
            return;
        }

        if (entity == _entity && combat.Level == _level && Equals(xp.Build, _source) && xp.DailyPlan.SequenceEqual(_sourcePlan))
            return;

        _entity = entity;
        _level = combat.Level;
        _source = xp.Build;
        _sourcePlan = xp.DailyPlan.Select(pick => pick.Copy()).ToList();
        _plan = xp.DailyPlan.Select(pick => pick.Copy()).ToList();

        _body.RemoveAllChildren();
        var slots = CFOptionChoices.DailySlots(_prototypes, xp.Build, xp.AncestryId, combat.Level);
        _status.Text = Loc.GetString(slots.Count == 0 ? "cf-daily-none" : "cf-daily-heading");
        foreach (var slot in slots)
        {
            _body.AddChild(Row(slot, xp));
        }
    }

    private Control Row(CFOptionSlot slot, CFExperienceComponent xp)
    {
        var today = xp.OptionPicks.FirstOrDefault(p => p.Source == slot.Source && p.Slot == slot.Slot)?.Value;
        var planned = _plan.FirstOrDefault(p => p.Source == slot.Source && p.Slot == slot.Slot)?.Value ?? today;
        var allowed = CFOptionChoices.Allowed(_prototypes, slot.Choice, xp.Build, xp.AncestryId);

        var button = new OptionButton { HorizontalExpand = true };
        button.AddItem(Loc.GetString("cf-levelup-choose"), 0);
        for (var i = 0; i < allowed.Count; i++)
        {
            button.AddItem(OptionLabel(allowed[i]), i + 1);
            if (allowed[i] == planned)
                button.SelectId(i + 1);
        }

        button.OnItemSelected += args =>
        {
            button.SelectId(args.Id);
            _plan.RemoveAll(p => p.Source == slot.Source && p.Slot == slot.Slot);
            if (args.Id > 0)
                _plan.Add(new CFOptionPick { Source = slot.Source, Slot = slot.Slot, Value = allowed[args.Id - 1] });

            var plan = new CFPlanDailyChoicesEvent();
            plan.Picks.AddRange(_plan.Select(pick => pick.Copy()));
            _entities.EntityNetManager.SendSystemNetworkMessage(plan);
        };

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8, HorizontalExpand = true };
        row.AddChild(new Label { Text = Loc.GetString(slot.SourceName), MinWidth = 220, VerticalAlignment = VAlignment.Center });
        row.AddChild(new Label
        {
            Text = Loc.GetString("cf-daily-today", ("value", today == null ? Loc.GetString("cf-daily-nothing") : OptionLabel(today))),
            MinWidth = 220,
            VerticalAlignment = VAlignment.Center,
        });
        row.AddChild(new Label { Text = Loc.GetString("cf-daily-next"), VerticalAlignment = VAlignment.Center });
        row.AddChild(button);
        return row;
    }

    private string OptionLabel(string value)
    {
        if (value.StartsWith(CFOptionChoices.SignaturePrefix, StringComparison.Ordinal))
            value = value[CFOptionChoices.SignaturePrefix.Length..];

        if (_prototypes.TryIndex<CFSpellPrototype>(value, out var spell))
            return spell.Name;

        if (_prototypes.TryIndex<CFAbilitySkillPrototype>(value, out var skill))
            return Loc.GetString(skill.Name);

        return value;
    }
}
