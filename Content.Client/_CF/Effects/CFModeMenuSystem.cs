using Content.Client.UserInterface.Controls;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Content.Shared.Popups;
using Robust.Shared.Maths;

namespace Content.Client.CF.Effects;

/// <summary>
/// An action with <see cref="CFModeChoiceComponent"/> asks which mode before it is used.
/// Immediate modes happen on pick. The others then wait for a click.
/// </summary>
public sealed partial class CFModeMenuSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFSpellSystem _spells = default!;

    private SimpleRadialMenu? _menu;

    public bool TryOffer(Entity<ActionComponent> action, Action begin)
    {
        if (!TryComp(action.Owner, out CFModeChoiceComponent? choice) || choice.Modes.Count == 0)
            return false;

        if (action.Comp.AttachedEntity is not { } user)
            return false;

        if (TryComp(action.Owner, out CFSpellComponent? spell) && !_spells.HasSlot(user, spell))
        {
            _popup.PopupEntity(Loc.GetString("cf-spell-no-slots"), user, user);
            return true;
        }

        var cost = _actions.ActionCount(action);
        if (_actions.ReadyCount(user) < cost)
        {
            _popup.PopupEntity(Loc.GetString("cf-action-not-enough"), user, user);
            return true;
        }

        var options = new List<RadialMenuOptionBase>(choice.Modes.Count);
        foreach (var mode in choice.Modes)
        {
            options.Add(Slice(action.Owner, mode, choice, begin));
        }

        _menu?.Close();
        _menu = new SimpleRadialMenu();
        _menu.SetButtons(options);
        _menu.OpenOverMouseScreenPosition();
        return true;
    }

    private RadialMenuActionOption<byte> Slice(EntityUid action, CFModeOption mode, CFModeChoiceComponent choice, Action begin)
    {
        return new RadialMenuActionOption<byte>(picked =>
        {
            choice.Chosen = picked;
            if (mode.Immediate)
            {
                RaisePredictiveEvent(new CFModeImmediateEvent(GetNetEntity(action), picked));
                return;
            }

            begin();
        }, mode.Value)
        {
            Text = Loc.GetString(mode.Name),
            ToolTip = mode.Tooltip != null ? Loc.GetString(mode.Tooltip) : null,
            Order = mode.Value,
            BackgroundColor = new Color(72, 48, 120, 190),
            HoverBackgroundColor = new Color(140, 110, 190, 220),
        };
    }
}
