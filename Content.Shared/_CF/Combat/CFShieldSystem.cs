using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
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

    /// <summary>The shield in the active hand, if any.</summary>
    public bool TryGetHeldShield(EntityUid user, out Entity<CFShieldComponent> shield)
    {
        shield = default;
        if (!_hands.TryGetActiveItem(user, out var held) || held == null)
            return false;

        if (!TryComp(held.Value, out CFShieldComponent? comp))
            return false;

        shield = (held.Value, comp);
        return true;
    }

    /// <summary>A shield that can be raised: held and not broken.</summary>
    public bool CanRaise(EntityUid user)
    {
        return TryGetHeldShield(user, out var shield) && !shield.Comp.Broken;
    }

    /// <summary>The circumstance bonus to AC a raised shield gives right now.</summary>
    public int RaisedBonus(EntityUid user)
    {
        if (!TryComp(user, out CFConditionComponent? cond) || !cond.RaisedShield)
            return 0;

        return TryGetHeldShield(user, out var shield) && !shield.Comp.Broken ? shield.Comp.AcBonus : 0;
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
            _popup.PopupEntity(Loc.GetString("cf-shield-broken", ("shield", shield.Owner)), user);
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
