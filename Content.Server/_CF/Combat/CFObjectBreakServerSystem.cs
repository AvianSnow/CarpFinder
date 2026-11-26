using Content.Server.Destructible;
using Content.Shared.CF.Combat;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.FixedPoint;

namespace Content.Server.CF.Combat;

/// <summary>
/// Breaks an object through its own damage thresholds, lowest first, as real damage would reach them.
/// </summary>
public sealed partial class CFObjectBreakServerSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;

    [SubscribeLocalEvent]
    private void OnBreak(Entity<DestructibleComponent> ent, ref CFBreakObjectEvent args)
    {
        var steps = new List<FixedPoint2>();
        foreach (var threshold in ent.Comp.Thresholds)
        {
            if (threshold.Trigger is DamageTrigger total && !steps.Contains(total.Damage))
                steps.Add(total.Damage);
        }

        steps.Sort();
        foreach (var step in steps)
        {
            var damage = new DamageSpecifier();
            damage.DamageDict["Blunt"] = step;
            _damageable.SetDamage(ent.Owner, damage);
            if (Deleted(ent) || EntityManager.IsQueuedForDeletion(ent))
                return;
        }
    }
}
