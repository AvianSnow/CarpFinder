using Content.Shared.Bed.Sleep;
using Content.Shared.CF.Character;
using Content.Shared.Actions.Events;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
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
    [Dependency] private CFCharacterFeatureSystem _features = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFShieldSystem _shields = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

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
        return Unmet(user, stance) == null;
    }

    /// <summary>The locale id saying what the stance needs and the creature lacks, or null when it has it all.</summary>
    public string? Unmet(EntityUid user, CFStanceComponent stance)
    {
        if (stance.RequiresUnarmored
            && _armor.TryGetWornArmor(user, out var armor)
            && armor.Comp.Category != CFArmorCategory.Unarmored)
        {
            return "cf-stance-needs-unarmored";
        }

        if (stance.RequiresRangedWeapon && !Holding<CFRangedWeaponComponent>(user))
            return "cf-stance-needs-ranged";

        if (stance.RequiresMeleeWeapon && !Holding<CFMeleeWeaponComponent>(user))
            return "cf-stance-needs-melee";

        if (stance.RequiresFreeHand && _hands.CountFreeHands(user) == 0)
            return "cf-stance-needs-free-hand";

        if (stance.RequiresShield && !_shields.TryGetUsableShield(user, out _))
            return "cf-stance-needs-shield";

        return null;
    }

    private bool Holding<T>(EntityUid user) where T : IComponent
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<T>(held))
                return true;
        }

        return false;
    }

    /// <summary>Picking up or letting go of something can end a stance that needs a weapon, a shield or a free hand.</summary>
    [SubscribeLocalEvent]
    private void OnHandEquipped(Entity<CFActiveStanceComponent> ent, ref DidEquipHandEvent args)
    {
        CheckHands(ent);
    }

    [SubscribeLocalEvent]
    private void OnHandUnequipped(Entity<CFActiveStanceComponent> ent, ref DidUnequipHandEvent args)
    {
        CheckHands(ent);
    }

    private void CheckHands(EntityUid user)
    {
        if (_net.IsServer && Current(user) is { } stance && !MeetsRequirements(user, stance))
            Leave(user);
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

        if (Unmet(args.User, ent.Comp) is { } unmet)
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString(unmet);
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
        _features.SetActionToggled(user, proto.ID, true);
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
        _features.SetActionToggled(user, id, false);
    }

    [SubscribeLocalEvent]
    private void OnMobState(MobStateChangedEvent args)
    {
        if (_net.IsServer && args.NewMobState != MobState.Alive)
            Leave(args.Target);
    }

    [SubscribeLocalEvent]
    private void OnFellAsleep(Entity<CFActiveStanceComponent> ent, ref SleepStateChangedEvent args)
    {
        if (_net.IsServer && args.FellAsleep)
            Leave(ent);
    }

    [SubscribeLocalEvent]
    private void OnPutToSleep(Entity<CFActiveStanceComponent> ent, ref CFPutToSleepEvent args)
    {
        if (_net.IsServer)
            Leave(ent);
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
