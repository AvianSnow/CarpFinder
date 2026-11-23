using Content.Client.Items;
using Content.Client.Message;
using Content.Client.Stylesheets;
using Content.Shared.CF.Actions;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.CF.Combat;

/// <summary>Shows which finisher a Swashbuckler's weapon is set to in the hand's status panel.</summary>
public sealed partial class CFFinisherClientSystem : EntitySystem
{
    [Dependency] private CFFinisherSystem _finishers = default!;
    [Dependency] private IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subs.ItemStatus<CFFinisherChoiceComponent>(ent => new CFFinisherStatusControl(ent, _finishers, _player));
    }
}

public sealed class CFFinisherStatusControl : Control
{
    private readonly Entity<CFFinisherChoiceComponent> _weapon;
    private readonly CFFinisherSystem _finishers;
    private readonly IPlayerManager _player;
    private readonly RichTextLabel _label;
    private string? _shown;

    public CFFinisherStatusControl(Entity<CFFinisherChoiceComponent> weapon, CFFinisherSystem finishers, IPlayerManager player)
    {
        _weapon = weapon;
        _finishers = finishers;
        _player = player;
        _label = new RichTextLabel { StyleClasses = { StyleClass.ItemStatus } };
        AddChild(_label);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_player.LocalEntity is not { } user)
            return;

        // Nothing for someone with no finishers who happens to hold a Swashbuckler's weapon.
        var text = _finishers.Known(user).Count == 0
            ? string.Empty
            : Loc.GetString("cf-finisher-status", ("finisher", _finishers.Selected(user, _weapon).Name));
        if (text == _shown)
            return;

        _shown = text;
        _label.SetMarkup(text);
    }
}
