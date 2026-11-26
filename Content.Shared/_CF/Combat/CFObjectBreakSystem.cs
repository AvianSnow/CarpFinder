using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;

namespace Content.Shared.CF.Combat;

/// <summary>
/// Breaks an object whose CF Hit Points ran out, or that a tool finished working on.
/// The server walks the object's own damage thresholds from the lowest up, so a tree drops its logs and a vein its
/// ore. One huge hit would fire whichever threshold is listed first, which is often the overkill one that drops nothing.
/// </summary>
public sealed partial class CFObjectBreakSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;

    public void Break(EntityUid? user, EntityUid target)
    {
        var ev = new CFBreakObjectEvent(user);
        RaiseLocalEvent(target, ref ev);
        if (Gone(target))
            return;

        // Nothing with thresholds answered, or none of them removed it.
        var finish = new DamageSpecifier();
        finish.DamageDict["Blunt"] = 10000;
        _damageable.TryChangeDamage(target, finish, ignoreResistances: true, origin: user);
        if (Gone(target))
            return;

        _destructible.BreakEntity(target);
        if (!Gone(target))
            _destructible.DestroyEntity(target);
    }

    private bool Gone(EntityUid target)
    {
        return Deleted(target) || EntityManager.IsQueuedForDeletion(target);
    }
}

/// <summary>Raised on an object that is to break now. The server answers it for anything with damage thresholds.</summary>
[ByRefEvent]
public readonly record struct CFBreakObjectEvent(EntityUid? User);
