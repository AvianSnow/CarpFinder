using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Stances: one at a time, and another stance action has to wait a round. A stance ends when the creature is knocked out,
/// when its requirement stops being met, when another stance action is used, or when the creature uses the same action to
/// Dismiss it. There are no encounters in real time, so one does not end by itself when a fight is over.
/// </summary>
public sealed partial class CFStanceSystem : EntitySystem
{
    private const string BonusSource = "stance";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFArmorSystem _armor = default!;

    /// <summary>The stance the creature is in, or null.</summary>
    public CFStanceComponent? Current(EntityUid user)
    {
        if (!TryComp(user, out CFActiveStanceComponent? active)
            || active.Stance is not { } id
            || !_prototypes.TryIndex(id, out var proto))
        {
            return null;
        }

        return proto.TryComp<CFStanceComponent>(out var stance, Factory) ? stance : null;
    }

    public bool MeetsRequirements(EntityUid user, CFStanceComponent stance)
    {
        return !stance.RequiresUnarmored
               || !_armor.TryGetWornArmor(user, out var armor)
               || armor.Comp.Category == CFArmorCategory.Unarmored;
    }

    /// <summary>Ends the creature's stance, if it has one.</summary>
    public void Leave(EntityUid user)
    {
        if (!TryComp(user, out CFActiveStanceComponent? active) || active.Stance == null)
            return;

        End(user, active);
        _popup.PopupEntity(Loc.GetString("cf-stance-leave"), user, user);
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFStanceComponent> ent, ref ActionAttemptEvent args)
    {
        var active = CompOrNull<CFActiveStanceComponent>(args.User);

        // Using the stance you are in Dismisses it, and Dismiss is not a stance action.
        if (active?.Stance?.Id == MetaData(ent).EntityPrototype?.ID)
            return;

        if (active != null && _timing.CurTime < active.NextStance)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-stance-too-soon");
            return;
        }

        if (!MeetsRequirements(args.User, ent.Comp))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("cf-stance-needs-unarmored");
        }
    }

    [SubscribeLocalEvent]
    private void OnPerform(Entity<CFStanceComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer || MetaData(ent).EntityPrototype is not { } proto)
            return;

        var user = args.Performer;
        var active = EnsureComp<CFActiveStanceComponent>(user);
        if (active.Stance?.Id == proto.ID)
        {
            Leave(user);
            return;
        }

        End(user, active);
        active.Stance = proto.ID;
        active.NextStance = _timing.CurTime + CFBonusSystem.Round;
        Dirty(user, active);

        foreach (var bonus in ent.Comp.Bonuses)
        {
            _bonuses.AddPermanentBonus(user, bonus.Stat, bonus.Type, bonus.Value, source: BonusSource);
        }

        _armor.RefreshArmorClass(user);
        SetToggled(user, proto.ID, true);
        _popup.PopupEntity(Loc.GetString("cf-stance-enter", ("name", MetaData(ent).EntityName)), user, user);
    }

    private void End(EntityUid user, CFActiveStanceComponent active)
    {
        if (active.Stance is not { } id)
            return;

        active.Stance = null;
        Dirty(user, active);
        _bonuses.RemoveBonusesFrom(user, BonusSource);
        _armor.RefreshArmorClass(user);
        SetToggled(user, id, false);
    }

    private void SetToggled(EntityUid user, string stanceId, bool toggled)
    {
        foreach (var action in _actions.GetActions(user))
        {
            if (MetaData(action).EntityPrototype?.ID != stanceId)
                continue;

            _actions.SetToggled((action, action.Comp), toggled);
            return;
        }
    }

    [SubscribeLocalEvent]
    private void OnMobState(MobStateChangedEvent args)
    {
        if (_net.IsServer && args.NewMobState != MobState.Alive)
            Leave(args.Target);
    }

    [SubscribeLocalEvent]
    private void OnEquipped(Entity<CFActiveStanceComponent> ent, ref DidEquipEvent args)
    {
        if (_net.IsServer && Current(ent) is { } stance && !MeetsRequirements(ent, stance))
            Leave(ent);
    }

    /// <summary>Stops the swing before it costs anything. Shots and thrown weapons are stopped in the Strike itself.</summary>
    [SubscribeLocalEvent]
    private void OnAttackAttempt(Entity<CFActiveStanceComponent> ent, ref AttackAttemptEvent args)
    {
        if (args.Disarm
            || args.Weapon is not { } weapon
            || weapon.Owner == ent.Owner
            || !HasComp<CFWeaponComponent>(weapon)
            || Current(ent) is not { OnlyStanceStrikes: true })
        {
            return;
        }

        args.Cancel();
        if (_net.IsServer)
            _popup.PopupEntity(Loc.GetString("cf-stance-only-strikes"), ent, ent);
    }
}
