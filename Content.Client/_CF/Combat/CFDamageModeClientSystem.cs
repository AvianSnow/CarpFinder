using Content.Client.Items;
using Content.Client.Message;
using Content.Client.Stylesheets;
using Content.Shared.CF.Combat;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.CF.Combat;

/// <summary>Shows a switchable weapon's damage type in the hand's status panel, as a multitool shows its tool.</summary>
public sealed partial class CFDamageModeClientSystem : EntitySystem
{
    [Dependency] private CFDamageModeSystem _modes = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subs.ItemStatus<CFDamageModeComponent>(ent => new CFDamageModeStatusControl(ent, _modes));
    }
}

public sealed class CFDamageModeStatusControl : Control
{
    private readonly Entity<CFDamageModeComponent> _weapon;
    private readonly CFDamageModeSystem _modes;
    private readonly RichTextLabel _label;
    private int _shown = -1;

    public CFDamageModeStatusControl(Entity<CFDamageModeComponent> weapon, CFDamageModeSystem modes)
    {
        _weapon = weapon;
        _modes = modes;
        _label = new RichTextLabel { StyleClasses = { StyleClass.ItemStatus } };
        AddChild(_label);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_shown == _weapon.Comp.Index)
            return;

        _shown = _weapon.Comp.Index;
        _label.SetMarkup(_modes.Markup(_weapon.Comp));
    }
}
