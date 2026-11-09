using Content.Shared.Actions.Events;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Time;
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
/// Focus Points: what focus spells cost. A long rest refills them all. Refocus gives one back after 10 minutes on the world
/// clock, and moving or being hurt interrupts it.
/// </summary>
public sealed partial class CFFocusSystem : EntitySystem
{
    /// <summary>A creature's focus pool never holds more than this.</summary>
    public const int MaxFocusPoints = 3;

    private const float RefocusWorldSeconds = 600f;

    /// <summary>The world clock's usual pace, for a map with no clock.</summary>
    private const float DefaultWorldRate = 4f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    public static int Remaining(CFSpellbookComponent book)
    {
        return Math.Max(0, book.FocusPoints - book.FocusSpent);
    }

    public TimeSpan RefocusTime()
    {
        var query = EntityQueryEnumerator<CFTimeComponent>();
        if (query.MoveNext(out var clock) && clock.TryGetRealSeconds(RefocusWorldSeconds, CFTimeScale.CarpFinder, out var seconds))
            return TimeSpan.FromSeconds(seconds);

        return TimeSpan.FromSeconds(RefocusWorldSeconds / DefaultWorldRate);
    }

    [SubscribeLocalEvent]
    private void OnAttempt(Entity<CFRefocusComponent> ent, ref ActionAttemptEvent args)
    {
        if (TryComp(args.User, out CFSpellbookComponent? book) && book.FocusSpent > 0)
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
        if (args.Cancelled || args.Handled || ent.Comp.FocusSpent <= 0)
            return;

        args.Handled = true;
        ent.Comp.FocusSpent--;
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("cf-focus-done"), ent, ent);
    }
}
