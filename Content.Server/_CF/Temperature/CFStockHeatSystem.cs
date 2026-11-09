using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server.CF.Temperature;

/// <summary>
/// Turns stock burning into CF fire damage. Stock fire, lava and body heat write to the SS14 damage bar, which is only a
/// mirror of CF HP, so without this a creature dies with full hit points and no resistance counts. Stock still owns
/// catching fire, spreading, putting it out and how hot it is.
/// </summary>
public sealed partial class CFStockHeatSystem : EntitySystem
{
    /// <summary>
    /// How much stock Heat one hit point is. Our choice: a mob takes 1.5 Heat a second per fire stack, so 2 stacks is
    /// about the 1d6 of a persistent fire effect each round. It is fixed, not a share of the creature's hit points,
    /// because PF2e fire hurts everyone the same.
    /// </summary>
    public const float HeatPerHp = 5f;

    private const string StockHeat = "Heat";

    private const string FireDamage = "Fire";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFEffectSystem _effects = default!;

    /// <summary>
    /// This event and not <c>DamageModifyEvent</c>, because body heat ignores resistances and skips that one. Damage from
    /// someone is a Strike or a spell, which CF already resolves, so only damage with no origin counts.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBeforeDamage(Entity<CFCombatantComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled
            || args.Origin != null
            || !HasComp<MobStateComponent>(ent)
            || args.Damage.DamageDict.Count != 1
            || !args.Damage.DamageDict.TryGetValue(StockHeat, out var heat)
            || heat <= 0)
            return;

        args.Cancelled = true;
        if (!TryComp(ent, out CFStockHeatComponent? stock))
        {
            stock = AddComp<CFStockHeatComponent>(ent);
            stock.NextTick = _timing.CurTime + CFBonusSystem.Round;
        }

        stock.Pending += heat.Float();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFStockHeatComponent>();
        while (query.MoveNext(out var uid, out var stock))
        {
            if (now < stock.NextTick)
                continue;

            stock.NextTick = now + CFBonusSystem.Round;
            var damage = (int) (stock.Pending / HeatPerHp);
            if (damage <= 0 || _mobState.IsDead(uid))
            {
                RemCompDeferred<CFStockHeatComponent>(uid);
                continue;
            }

            // Once a round, as persistent damage is, so a downed creature gains dying once and not every second.
            stock.Pending -= damage * HeatPerHp;
            _effects.DealDamage(uid, uid, damage, FireDamage, false);
        }
    }
}
