using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Spells;

/// <summary>Marks the Refocus action.</summary>
[RegisterComponent]
public sealed partial class CFRefocusComponent : Component
{
}

[Serializable, NetSerializable]
public sealed partial class CFRefocusDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Focus Points: what focus spells cost. A long rest refills them all. Refocus gives one back after a minute of real time,
/// where the rules take 10 minutes, and moving or being hurt interrupts it.
/// </summary>
public sealed partial class CFFocusSystem : EntitySystem
{
    /// <summary>A creature's focus pool never holds more than this.</summary>
    public const int MaxFocusPoints = 3;

    /// <summary>How long Refocus takes, in real seconds. It does not follow the world clock.</summary>
    public const float RefocusSeconds = 60f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    public static int Remaining(CFSpellbookComponent book)
    {
        return Math.Max(0, book.FocusPoints - book.FocusSpent);
    }

    public TimeSpan RefocusTime()
    {
        return TimeSpan.FromSeconds(RefocusSeconds);
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFRefocusComponent> ent, ref ActionAttemptEvent args)
    {
        if (TryComp(args.User, out CFSpellbookComponent? book) && book.FocusSpent > 0)
            return;

        // An Oracle also Refocuses to ease its curse.
        if (EntityManager.System<CFCurseboundSystem>().Value(args.User) > 0)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-focus-full");
    }

    [SubscribeLocalEvent]
    private void OnRefocus(Entity<CFRefocusComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_net.IsServer)
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.Performer, RefocusTime(), new CFRefocusDoAfterEvent(), args.Performer)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("cf-focus-start"), args.Performer, args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnRefocused(Entity<CFSpellbookComponent> ent, ref CFRefocusDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var curses = EntityManager.System<CFCurseboundSystem>();
        if (ent.Comp.FocusSpent <= 0 && curses.Value(ent) <= 0)
            return;

        args.Handled = true;
        curses.Lower(ent);
        if (ent.Comp.FocusSpent > 0)
            ent.Comp.FocusSpent--;

        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("cf-focus-done"), ent, ent);
    }
}
