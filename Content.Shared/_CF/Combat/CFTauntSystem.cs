using Content.Shared.CF.Actions;
using Content.Shared.CF.Factions;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Combat;

/// <summary>On a creature that has been Taunted.</summary>
[RegisterComponent]
public sealed partial class CFTauntedComponent : Component
{
    [DataField]
    public EntityUid Taunter;

    [DataField]
    public TimeSpan Until;

    /// <summary>Until this time, the creature is off-guard for having ignored the Taunt. Proud Nail reads it.</summary>
    [DataField]
    public TimeSpan IgnoredUntil;
}

/// <summary>Who this creature has Taunted. Only one at a time.</summary>
[RegisterComponent]
public sealed partial class CFTauntingComponent : Component
{
    [DataField]
    public EntityUid? Target;
}

/// <summary>An action that Taunts its target. <see cref="CFTauntSystem"/> runs it.</summary>
[RegisterComponent]
public sealed partial class CFTauntActionComponent : Component
{
    /// <summary>Also raises the shield in hand (Shielding Taunt).</summary>
    [DataField]
    public bool RaisesShield;
}

/// <summary>
/// The Guardian's Taunt. A taunted enemy that attacks one of the taunter's allies, leaving the taunter out, takes a penalty on
/// that attack and is off-guard. A Taunt lasts 6 seconds, the same as anything else that runs "until your next turn".
/// </summary>
public sealed partial class CFTauntSystem : EntitySystem
{
    private const float Seconds = 6f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFShieldSystem _shields = default!;

    public void Taunt(EntityUid user, EntityUid target)
    {
        var taunting = EnsureComp<CFTauntingComponent>(user);
        if (taunting.Target is { } old && old != target && TryComp(old, out CFTauntedComponent? previous) && previous.Taunter == user)
            RemComp<CFTauntedComponent>(old);

        taunting.Target = target;
        var taunted = EnsureComp<CFTauntedComponent>(target);
        taunted.Taunter = user;
        taunted.Until = _timing.CurTime + TimeSpan.FromSeconds(Seconds);
        if (_net.IsServer)
            _popup.PopupEntity(Loc.GetString("cf-act-taunt"), target);
    }

    /// <summary>True while <paramref name="enemy"/> is under <paramref name="taunter"/>'s Taunt.</summary>
    public bool IsTauntedBy(EntityUid enemy, EntityUid taunter)
    {
        return TryComp(enemy, out CFTauntedComponent? taunted) && taunted.Taunter == taunter && _timing.CurTime < taunted.Until;
    }

    /// <summary>True while <paramref name="enemy"/> is off-guard for ignoring this creature's Taunt.</summary>
    public bool HasIgnored(EntityUid enemy, EntityUid taunter)
    {
        return TryComp(enemy, out CFTauntedComponent? taunted) && taunted.Taunter == taunter && _timing.CurTime < taunted.IgnoredUntil;
    }

    /// <summary>Call as <paramref name="attacker"/> makes an attack or forces a save. Returns the modifier for that roll or DC.</summary>
    public int NoteAttack(EntityUid attacker, EntityUid target)
    {
        if (!_net.IsServer
            || !TryComp(attacker, out CFTauntedComponent? taunted)
            || _timing.CurTime >= taunted.Until
            || target == taunted.Taunter
            || TerminatingOrDeleted(taunted.Taunter)
            || _factions.CanHarm(taunted.Taunter, target))
        {
            return 0;
        }

        taunted.IgnoredUntil = _timing.CurTime + TimeSpan.FromSeconds(Seconds);
        _conditions.SetOffGuardAll(attacker, Seconds);
        return -1;
    }

    [SubscribeLocalEvent]
    private void OnPerform(Entity<CFTauntActionComponent> ent, ref CFActivityTargetEvent args)
    {
        if (args.Handled)
            return;

        if (!_factions.CanHarm(args.Performer, args.Target))
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-act-cannot"), args.Performer, args.Performer);

            return;
        }

        args.Handled = true;
        if (!_net.IsServer)
            return;

        if (ent.Comp.RaisesShield && _shields.TryGetUsableShield(args.Performer, out var shield))
            _shields.SetRaised(shield, true);

        Taunt(args.Performer, args.Target);
    }
}
