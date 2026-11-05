using Content.Server.Instruments;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server.CF.Actions;

/// <summary>
/// A Bard playing a held instrument performs Courageous Anthem, and keeps paying for it while the music lasts.
/// Instruments are server-only, so this cannot sit with the other activities.
/// </summary>
public sealed partial class CFAnthemSystem : EntitySystem
{
    /// <summary>The anthem's bonus lasts as long, so steady playing never drops it.</summary>
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(CFActivitySystem.AnthemSeconds);

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InstrumentSystem _instruments = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFActivitySystem _activities = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<InstrumentComponent>();
        while (query.MoveNext(out var uid, out var instrument))
        {
            if (instrument.Playing
                && _instruments.GetInstrumentPlayer(uid) is { } player
                && _hands.IsHolding(player, uid))
                TryPlay(player);
        }
    }

    /// <summary>Pays one action for the anthem, unless the last one is still running. Returns whether it played.</summary>
    public bool TryPlay(EntityUid player)
    {
        if (!TryComp(player, out CFCombatOptionsComponent? options) || !options.Anthem)
            return false;

        var now = _timing.CurTime;
        if (now < options.NextAnthem)
            return false;

        if (!_actions.TrySpendActions(player, 1))
        {
            options.NextAnthem = now + RetryInterval;
            return false;
        }

        options.NextAnthem = now + RepeatInterval;
        _activities.Anthem(player);
        return true;
    }
}
