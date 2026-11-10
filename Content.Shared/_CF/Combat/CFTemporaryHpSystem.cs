using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Temporary Hit Points. Damage takes them first. They do not stack: a new grant only counts when it is higher, and then
/// it replaces the old one and its duration.
/// </summary>
public sealed partial class CFTemporaryHpSystem : EntitySystem
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (!_net.IsServer || now < _nextCheck)
            return;

        _nextCheck = now + CheckEvery;
        var query = EntityQueryEnumerator<CFCombatantComponent>();
        while (query.MoveNext(out var uid, out var combat))
        {
            if (combat.TemporaryHp > 0 && combat.TemporaryHpUntil != TimeSpan.Zero && now >= combat.TemporaryHpUntil)
                Set(uid, combat, 0, TimeSpan.Zero, null);
        }
    }

    /// <summary>Gives temporary Hit Points. <paramref name="seconds"/> of 0 lasts until spent or cleared by its source.</summary>
    /// <returns>False if the creature already has as many.</returns>
    public bool Grant(EntityUid uid, int amount, float seconds, string? source = null)
    {
        if (amount <= 0 || !TryComp(uid, out CFCombatantComponent? combat) || amount <= combat.TemporaryHp)
            return false;

        var until = seconds > 0 ? _timing.CurTime + TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        Set(uid, combat, amount, until, source);
        return true;
    }

    /// <summary>Takes damage from temporary Hit Points first. Returns what is left for real Hit Points.</summary>
    public int Absorb(EntityUid uid, CFCombatantComponent combat, int damage)
    {
        if (damage <= 0 || combat.TemporaryHp <= 0)
            return damage;

        var used = Math.Min(damage, combat.TemporaryHp);
        var left = combat.TemporaryHp - used;
        Set(uid, combat, left, left > 0 ? combat.TemporaryHpUntil : TimeSpan.Zero, left > 0 ? combat.TemporaryHpSource : null);
        return damage - used;
    }

    /// <summary>Ends temporary Hit Points that came from <paramref name="source"/>, as ending a Rage ends its own.</summary>
    public void Clear(EntityUid uid, string source)
    {
        if (TryComp(uid, out CFCombatantComponent? combat) && combat.TemporaryHpSource == source)
            Set(uid, combat, 0, TimeSpan.Zero, null);
    }

    private void Set(EntityUid uid, CFCombatantComponent combat, int amount, TimeSpan until, string? source)
    {
        combat.TemporaryHp = amount;
        combat.TemporaryHpUntil = until;
        combat.TemporaryHpSource = source;
        Dirty(uid, combat);
    }
}
