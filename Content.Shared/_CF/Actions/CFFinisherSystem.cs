using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Combat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

/// <summary>
/// The finisher a Swashbuckler's weapon is set to. Using the weapon in hand goes to the next finisher its holder knows,
/// and Alt+Z performs the one set. Null is Confident Finisher, which every Swashbuckler has. See <see cref="CFFinisherSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFFinisherChoiceComponent : Component
{
    /// <summary>The prototype of the finisher's action, or null for Confident Finisher.</summary>
    [DataField, AutoNetworkedField]
    public EntProtoId? Selected;
}

/// <summary>One finisher a creature can set its weapon to.</summary>
public readonly record struct CFKnownFinisher(EntProtoId? Id, string Name, Entity<ActionComponent>? Action);

/// <summary>
/// A Swashbuckler's finishers live on the weapon, not on the action wheel. Use in hand (Z) on a melee weapon switches
/// which finisher it is set to, for free, and Alt+Z performs it at the enemy being aimed at. A finisher is Confident
/// Finisher or any action the creature has with <c>finisher: true</c> on its <see cref="CFStrikeActionComponent"/>.
/// </summary>
public sealed partial class CFFinisherSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFManeuverSystem _maneuvers = default!;

    /// <summary>The finishers a creature knows, Confident Finisher first. Empty for a creature with no panache to spend.</summary>
    public List<CFKnownFinisher> Known(EntityUid user)
    {
        var known = new List<CFKnownFinisher>();
        if (CompOrNull<CFCombatOptionsComponent>(user)?.PanacheStyle != true)
            return known;

        known.Add(new CFKnownFinisher(null, Loc.GetString("cf-finisher-confident"), null));
        foreach (var action in _actions.GetActions(user))
        {
            if (CompOrNull<CFStrikeActionComponent>(action)?.Finisher != true
                || MetaData(action).EntityPrototype is not { } proto)
            {
                continue;
            }

            known.Add(new CFKnownFinisher(proto.ID, MetaData(action).EntityName, action));
        }

        return known;
    }

    /// <summary>The finisher this weapon is set to for its holder. One the holder does not know counts as Confident Finisher.</summary>
    public CFKnownFinisher Selected(EntityUid user, EntityUid weapon)
    {
        var known = Known(user);
        var wanted = CompOrNull<CFFinisherChoiceComponent>(weapon)?.Selected;
        foreach (var finisher in known)
        {
            if (finisher.Id == wanted)
                return finisher;
        }

        return known.Count > 0 ? known[0] : new CFKnownFinisher(null, Loc.GetString("cf-finisher-confident"), null);
    }

    /// <summary>Sets the weapon to the next finisher its holder knows. False when there is nothing to switch to.</summary>
    public bool Next(EntityUid user, EntityUid weapon)
    {
        var known = Known(user);
        if (known.Count < 2)
            return false;

        var current = Selected(user, weapon);
        var index = known.FindIndex(finisher => finisher.Id == current.Id);
        var next = known[(index + 1) % known.Count];
        var choice = EnsureComp<CFFinisherChoiceComponent>(weapon);
        choice.Selected = next.Id;
        Dirty(weapon, choice);
        if (_timing.IsFirstTimePredicted)
            _popup.PopupEntity(Loc.GetString("cf-finisher-set", ("finisher", next.Name)), user, user);

        return true;
    }

    // After a shield's raise and a modular weapon's damage type: those keep the key on the items that have them.
    [SubscribeLocalEvent(after: [typeof(CFShieldSystem), typeof(CFDamageModeSystem)])]
    private void OnUseInHand(Entity<CFMeleeWeaponComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = Next(args.User, ent);
    }

    /// <summary>A Swashbuckler who picks a weapon up sees which finisher it is set to.</summary>
    [SubscribeLocalEvent]
    private void OnPickedUp(Entity<CFMeleeWeaponComponent> ent, ref Content.Shared.Hands.GotEquippedHandEvent args)
    {
        if (_net.IsServer && CompOrNull<CFCombatOptionsComponent>(args.User)?.PanacheStyle == true)
            EnsureComp<CFFinisherChoiceComponent>(ent);
    }

    /// <summary>Performs the finisher the held weapon is set to, at the enemy being aimed at. It needs panache.</summary>
    public bool TryUse(EntityUid user)
    {
        if (!_net.IsServer
            || CompOrNull<CFCombatOptionsComponent>(user)?.Panache != true
            || !_hands.TryGetActiveItem(user, out var weapon)
            || !HasComp<CFMeleeWeaponComponent>(weapon.Value))
        {
            return false;
        }

        var finisher = Selected(user, weapon.Value);
        if (finisher.Action is not { } action)
            return _maneuvers.TryFinisher(user);

        if (!_maneuvers.TryAimedStrikeTarget(user, out var target))
            return false;

        var attempt = new ActionAttemptEvent(user, target);
        RaiseLocalEvent(action, ref attempt);
        if (attempt.Cancelled)
        {
            if (attempt.Reason != null)
                _popup.PopupEntity(attempt.Reason, user, user);

            return false;
        }

        _actions.PerformAction(user, action, new CFActivityTargetEvent { Target = target }, predicted: false);
        return true;
    }
}
