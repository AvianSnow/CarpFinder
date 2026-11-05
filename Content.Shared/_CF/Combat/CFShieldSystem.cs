using Content.Shared.CF.Actions;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Network;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Raise a Shield's AC bonus and Shield Block, with shield hit points, broken, and destroyed.
/// </summary>
public sealed partial class CFShieldSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private Content.Shared.Movement.Systems.MovementSpeedModifierSystem _speed = default!;

    /// <summary>A shield in either hand that is not broken.</summary>
    public bool TryGetUsableShield(EntityUid user, out Entity<CFShieldComponent> shield)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!TryComp(held, out CFShieldComponent? comp) || comp.Broken)
                continue;

            shield = (held, comp);
            return true;
        }

        shield = default;
        return false;
    }

    /// <summary>The raised shield in either hand, if any. The one check for "is their shield up".</summary>
    public bool TryGetRaisedShield(EntityUid user, out Entity<CFShieldComponent> shield)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!TryComp(held, out CFShieldComponent? comp) || !comp.Raised || comp.Broken)
                continue;

            shield = (held, comp);
            return true;
        }

        shield = default;
        return false;
    }

    public void SetRaised(Entity<CFShieldComponent> shield, bool raised)
    {
        if (shield.Comp.Raised == raised)
            return;

        shield.Comp.Raised = raised;
        if (!raised)
            shield.Comp.CoverPosition = null;

        Dirty(shield);
    }

    /// <summary>The circumstance bonus to AC the raised shield gives right now.</summary>
    public int RaisedBonus(EntityUid user)
    {
        if (!TryGetRaisedShield(user, out var shield))
            return 0;

        return InCover(user, shield) ? shield.Comp.TakeCoverBonus : shield.Comp.AcBonus;
    }

    private bool InCover(EntityUid user, Entity<CFShieldComponent> shield)
    {
        return shield.Comp.CoverPosition is { } spot
               && spot.TryDistance(EntityManager, Transform(user).Coordinates, out var distance)
               && distance < 0.5f;
    }

    /// <summary>Any attack roll ends Take Cover.</summary>
    public void EndCover(EntityUid user)
    {
        if (TryGetRaisedShield(user, out var shield) && shield.Comp.CoverPosition != null)
        {
            shield.Comp.CoverPosition = null;
            Dirty(shield);
        }
    }

    [SubscribeLocalEvent]
    private void OnUseInHand(Entity<CFShieldComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (ent.Comp.Raised)
        {
            SetRaised(ent, false);
            _popup.PopupEntity(Loc.GetString("cf-act-shield-lowered"), args.User, args.User);
            return;
        }

        if (ent.Comp.Broken || !_actions.TrySpendActions(args.User, 1))
            return;

        SetRaised(ent, true);
        _popup.PopupEntity(Loc.GetString("cf-act-shield-raised"), args.User, args.User);
    }

    [SubscribeLocalEvent]
    private void OnUnequipped(Entity<CFShieldComponent> ent, ref GotUnequippedHandEvent args)
    {
        SetRaised(ent, false);
        _speed.RefreshMovementSpeedModifiers(args.User);
    }

    [SubscribeLocalEvent]
    private void OnEquipped(Entity<CFShieldComponent> ent, ref GotEquippedHandEvent args)
    {
        _speed.RefreshMovementSpeedModifiers(args.User);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<CFShieldComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (ent.Comp.TakeCoverBonus <= 0 || !ent.Comp.Raised || ent.Comp.CoverPosition != null || !_hands.IsHolding(args.User, ent.Owner))
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("cf-shield-take-cover"),
            Act = () => TakeCover(ent, user),
        });
    }

    public bool TakeCover(Entity<CFShieldComponent> shield, EntityUid user)
    {
        if (!shield.Comp.Raised || !_actions.TrySpendActions(user, 1))
            return false;

        shield.Comp.CoverPosition = Transform(user).Coordinates;
        Dirty(shield);
        return true;
    }

    /// <summary>
    /// Shield Block: the damage is reduced by the shield's Hardness, then both the shield and the creature
    /// take what is left. Returns the damage that still reaches the creature.
    /// </summary>
    public int Block(EntityUid user, Entity<CFShieldComponent> shield, int damage)
    {
        if (damage <= 0)
            return 0;

        var blocked = Math.Min(damage, shield.Comp.Hardness);
        var through = damage - blocked;
        if (blocked > 0)
            _popup.PopupEntity(Loc.GetString("cf-act-shield-block", ("amount", blocked)), user);

        if (through > 0)
            DamageShield(user, shield, through);

        return through;
    }

    public void DamageShield(EntityUid user, Entity<CFShieldComponent> shield, int amount)
    {
        var wasBroken = shield.Comp.Broken;
        shield.Comp.Hp = Math.Max(0, shield.Comp.CurrentHp - amount);
        Dirty(shield);

        if (shield.Comp.Hp <= 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-shield-destroyed", ("shield", shield.Owner)), user);
            if (_net.IsServer)
                QueueDel(shield);
            return;
        }

        if (!wasBroken && shield.Comp.Broken)
        {
            SetRaised(shield, false);
            _popup.PopupEntity(Loc.GetString("cf-shield-broken", ("shield", shield.Owner)), user);
        }
    }

    [SubscribeLocalEvent]
    private void OnExamine(Entity<CFShieldComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(
            ent.Comp.Broken ? "cf-shield-examine-broken" : "cf-shield-examine",
            ("hp", ent.Comp.CurrentHp),
            ("max", ent.Comp.MaxHp),
            ("bt", ent.Comp.BrokenThreshold),
            ("hardness", ent.Comp.Hardness),
            ("ac", ent.Comp.AcBonus)));
    }
}
