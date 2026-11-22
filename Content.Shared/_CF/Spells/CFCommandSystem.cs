using Content.Shared.CF.Combat;
using Content.Shared.CF.Languages;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>
/// Gives a Command order. Drop and Fall happen here and now. Approach, Flee and Halt add
/// <see cref="CFCommandedComponent"/>, which the server's AI carries out.
/// </summary>
public sealed partial class CFCommandSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFLanguageSystem _languages = default!;

    public static string OrderName(CFCommandOrder order)
    {
        return order switch
        {
            CFCommandOrder.Approach => "cf-command-approach",
            CFCommandOrder.Drop => "cf-command-drop",
            CFCommandOrder.Fall => "cf-command-fall",
            CFCommandOrder.Flee => "cf-command-flee",
            _ => "cf-command-halt",
        };
    }

    /// <summary>
    /// Command is linguistic: a creature that does not understand the caster's language ignores it. A caster who
    /// speaks nothing, or a target with no language data, is not checked.
    /// </summary>
    public bool Understands(EntityUid commander, EntityUid target)
    {
        return !_languages.TryGetSpoken(commander, out var language) || _languages.Understands(target, language.ID);
    }

    public void Give(EntityUid commander, EntityUid target, CFCommandOrder order, float seconds, int dc)
    {
        if (order == CFCommandOrder.None)
            return;

        if (!Understands(commander, target))
        {
            _popup.PopupEntity(Loc.GetString("cf-command-not-understood", ("target", target)), target, commander);
            return;
        }

        _popup.PopupEntity(Loc.GetString("cf-command-obeys", ("order", Loc.GetString(OrderName(order)))), target);

        switch (order)
        {
            case CFCommandOrder.Drop:
                foreach (var held in _hands.EnumerateHeld(target))
                {
                    _hands.TryDrop(target, held, checkActionBlocker: false);
                }

                return;
            case CFCommandOrder.Fall:
                _conditions.ApplyCondition(target, CFConditionKind.Prone, 1, seconds, commander, dc);
                return;
        }

        var commanded = EnsureComp<CFCommandedComponent>(target);
        commanded.Order = order;
        commanded.Commander = commander;
        commanded.Until = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        commanded.Holding = false;
    }

    /// <summary>The order a creature is obeying now, or None.</summary>
    public CFCommandOrder Current(EntityUid uid)
    {
        return TryComp(uid, out CFCommandedComponent? commanded) && _timing.CurTime < commanded.Until
            ? commanded.Order
            : CFCommandOrder.None;
    }
}
