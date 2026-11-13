using Content.Server.Instruments;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server.CF.Actions;

/// <summary>
/// A Bard playing a held instrument performs Courageous Anthem for as long as the music lasts. Starting costs 1 action;
/// keeping it going is free. Instruments are server-only, so this cannot sit with the other activities.
/// </summary>
public sealed partial class CFAnthemSystem : EntitySystem
{
    /// <summary>How long the anthem outlasts the music, so a short gap between notes or songs does not end it.</summary>
    public static readonly TimeSpan KeepUp = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InstrumentSystem _instruments = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFActivitySystem _activities = default!;
    [Dependency] private CFAuraSystem _auras = default!;

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

    /// <summary>
    /// Keeps the anthem going while the Bard plays. Starting it pays 1 action. Returns whether it started now, and so
    /// paid; keeping a running anthem going is free and returns false.
    /// </summary>
    public bool TryPlay(EntityUid player)
    {
        if (!TryComp(player, out CFCombatOptionsComponent? options) || !options.Anthem)
            return false;

        if (_auras.AnthemActive(player))
        {
            _auras.Anthem(player, KeepUp);
            return false;
        }

        var now = _timing.CurTime;
        if (now < options.NextAnthem)
            return false;

        if (!_actions.TrySpendActions(player, 1))
        {
            options.NextAnthem = now + RetryInterval;
            return false;
        }

        _activities.Anthem(player, KeepUp);
        return true;
    }
}
