using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Runs <see cref="CFModeOption.Immediate"/> modes, which happen on pick with no click.
/// Modes that wait for a click arrive through the action's own target event and <see cref="CFModeTargetEvent"/>.
/// </summary>
public sealed partial class CFModeChoiceSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [EventSubscription]
    private void OnImmediate(CFModeImmediateEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        var action = GetEntity(ev.Action);
        if (!TryComp(action, out CFModeChoiceComponent? choice) || !TryComp(action, out ActionComponent? actionComp))
            return;

        if (actionComp.AttachedEntity != user || _actions.IsCooldownActive(actionComp))
            return;

        choice.Chosen = ev.Mode;
        if (choice.ChosenOption() is not { Immediate: true })
            return;

        var attempt = new ActionAttemptEvent(user, null);
        RaiseLocalEvent(action, ref attempt);
        if (attempt.Cancelled)
        {
            if (_net.IsServer && attempt.Reason != null)
                _popup.PopupEntity(attempt.Reason, user, user);

            return;
        }

        var cast = new CFModeImmediateCastEvent(user, ev.Mode);
        RaiseLocalEvent(action, ref cast);
        if (!cast.Success)
            return;

        _actions.StartUseDelay((action, actionComp));
        var performed = new ActionPerformedEvent(user, null);
        RaiseLocalEvent(action, ref performed);
    }
}

/// <summary>Client to server: the user picked an immediate mode from the menu.</summary>
[Serializable, NetSerializable]
public sealed class CFModeImmediateEvent : EntityEventArgs
{
    public NetEntity Action;
    public byte Mode;

    public CFModeImmediateEvent(NetEntity action, byte mode)
    {
        Action = action;
        Mode = mode;
    }
}

/// <summary>Raised on the action for an immediate mode. Set <see cref="Success"/> when it happened.</summary>
[ByRefEvent]
public record struct CFModeImmediateCastEvent(EntityUid User, byte Mode)
{
    public bool Success;
}

/// <summary>Raised on the action when a mode that needs a click lands on <see cref="Target"/>.</summary>
[ByRefEvent]
public record struct CFModeTargetEvent(EntityUid User, EntityUid Target, byte Mode)
{
    public bool Handled;
    public bool Success;
}
