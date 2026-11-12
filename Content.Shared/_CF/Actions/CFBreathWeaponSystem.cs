using System.Diagnostics.CodeAnalysis;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Breath weapons: an area aimed at a point, then a rolled recharge. A player aims the action. The AI calls
/// <see cref="TryBreathe"/>, which performs the same action, so both pay its actions through <c>CFRadialSlot</c> and get
/// the same recharge.
/// </summary>
public sealed partial class CFBreathWeaponSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFActionPoolSystem _pool = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFBreathFlowSystem _flow = default!;
    [Dependency] private CFBreathGasSystem _gas = default!;
    [Dependency] private CFVfxSystem _vfx = default!;

    [SubscribeLocalEvent]
    private void OnBreathe(Entity<CFBreathWeaponComponent> ent, ref CFBreathWeaponEvent args)
    {
        if (args.Handled)
            return;

        var aim = _transform.ToMapCoordinates(args.Target);
        if (!_effects.ValidateArea(ent, args.Performer, aim, out var failure))
        {
            if (failure != null && _net.IsServer)
                _popup.PopupEntity(Loc.GetString(failure), args.Performer, args.Performer);
            return;
        }

        // Handled, so the action pays its actions and starts the use delay set below.
        args.Handled = true;
        if (!_net.IsServer)
            return;

        _vfx.Cast(args.Performer, ent);
        // A gas or a liquid breath takes time to spread, and its creatures roll as it reaches them.
        if (!_gas.TryStart((ent.Owner, null), args.Performer, aim) && !_flow.TryStart(ent, args.Performer, aim))
            _effects.ResolveArea(ent, args.Performer, aim);
        _actions.SetUseDelay(ent.Owner, RollRecharge(ent.Comp));
    }

    [SubscribeLocalEvent]
    private void OnCritical(Entity<CFDraconicMomentumComponent> ent, ref CFLandedCriticalEvent args)
    {
        if (!_net.IsServer)
            return;

        foreach (var action in _actions.GetActions(ent))
        {
            if (HasComp<CFBreathWeaponComponent>(action))
                _actions.RemoveCooldown(action.AsNullable());
        }
    }

    public TimeSpan RollRecharge(CFBreathWeaponComponent breath)
    {
        var rounds = 0;
        for (var i = 0; i < breath.RechargeDice; i++)
        {
            rounds += _random.Next(1, breath.RechargeDie + 1);
        }

        return TimeSpan.FromSeconds(rounds * breath.RoundSeconds);
    }

    /// <summary>The creature's first breath weapon, charged or not.</summary>
    public bool TryGetBreath(EntityUid user, [NotNullWhen(true)] out Entity<ActionComponent>? breath)
    {
        foreach (var action in _actions.GetActions(user))
        {
            if (!HasComp<CFBreathWeaponComponent>(action))
                continue;

            breath = action;
            return true;
        }

        breath = null;
        return false;
    }

    /// <summary>Charged, and the creature has the actions to pay for it now.</summary>
    public bool IsReady(EntityUid user, Entity<ActionComponent> breath)
    {
        return !_actions.IsCooldownActive(breath.Comp) && _pool.CanAffordActions(user, _pool.ActionCount(breath));
    }

    /// <summary>Whether breathing at the target now would catch it, by the same tiles and filter the breath uses.</summary>
    public bool WouldCatch(EntityUid user, EntityUid breath, EntityUid target)
    {
        if (!TryComp(breath, out CFAreaEffectComponent? area))
            return false;

        var caught = _effects.CollectArea(
            user,
            area.Shape,
            area.SizeFeet,
            _transform.GetMapCoordinates(user),
            _transform.GetMapCoordinates(target),
            area.StartsAtPoint(),
            area.IncludeSelf,
            area.IncludeSelf,
            area.Filter,
            area.Whitelist,
            area.MaxTargets);
        return caught.Contains(target);
    }

    /// <summary>Breathes at a point, for code with no player. False if it is recharging, unaffordable or out of reach.</summary>
    public bool TryBreathe(EntityUid user, Entity<ActionComponent> breath, MapCoordinates aim)
    {
        if (!IsReady(user, breath) || !_effects.ValidateArea(breath, user, aim, out _))
            return false;

        var ev = new CFBreathWeaponEvent
        {
            Target = _transform.ToCoordinates(aim),
        };
        _actions.PerformAction(user, breath, ev);
        return ev.Handled;
    }
}
