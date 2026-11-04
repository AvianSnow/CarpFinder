using Content.Shared.Actions.Events;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Spells;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Actions;

/// <summary>
/// Spends action stamina on strikes, shots, and radial activities, then returns one point every 1.8 seconds.
/// This is not <c>StaminaComponent</c>: that pool paralyzes a creature at the cap and is shared with stun weapons.
/// </summary>
public sealed partial class CFActionPoolSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<CFActionPoolComponent>();
        while (query.MoveNext(out var uid, out var pool))
        {
            var max = MaxStamina(pool);
            if (pool.Stamina >= max || pool.RegenSeconds <= 0f)
            {
                pool.RegenProgress = 0f;
                // A full pool is a new turn. MAP does not clear on a partial refill.
                if (pool.Stamina >= max && pool.MapAttacks != 0)
                {
                    pool.MapAttacks = 0;
                    Dirty(uid, pool);
                    RaiseLocalEvent(uid, new CFTurnRefilledEvent());
                }

                continue;
            }

            pool.RegenProgress += frameTime;
            if (pool.RegenProgress < pool.RegenSeconds)
                continue;

            var gained = (int) (pool.RegenProgress / pool.RegenSeconds);
            pool.RegenProgress -= gained * pool.RegenSeconds;
            pool.Stamina = Math.Min(max, pool.Stamina + gained);
            var refilled = pool.Stamina >= max;
            if (refilled)
                pool.MapAttacks = 0;
            Dirty(uid, pool);
            if (refilled)
                RaiseLocalEvent(uid, new CFTurnRefilledEvent());
        }
    }

    /// <summary>
    /// Gives the creature a pool if it has none. A negative stamina value starts full.
    /// </summary>
    public void Ensure(EntityUid uid)
    {
        var pool = EnsureComp<CFActionPoolComponent>(uid);
        if (pool.Stamina >= 0f)
            return;

        pool.Stamina = MaxStamina(pool);
        Dirty(uid, pool);
    }

    /// <summary>
    /// Changes how many actions this creature can hold.
    /// Raising the cap grants the new stamina immediately, so haste's extra action is usable at once.
    /// Lowering the cap clamps what they currently hold.
    /// </summary>
    public void SetMaxActions(EntityUid uid, int max, CFActionPoolComponent? pool = null)
    {
        if (!Resolve(uid, ref pool))
            return;

        max = Math.Max(0, max);
        var oldMax = MaxStamina(pool);
        pool.MaxActions = max;
        var newMax = MaxStamina(pool);
        if (pool.Stamina < 0f)
            pool.Stamina = newMax;
        else if (newMax > oldMax)
            pool.Stamina += newMax - oldMax;

        pool.Stamina = Math.Clamp(pool.Stamina, 0f, newMax);
        Dirty(uid, pool);
    }

    public static float MaxStamina(CFActionPoolComponent pool)
    {
        return Math.Max(0, pool.MaxActions) * Math.Max(0f, pool.StaminaPerAction);
    }

    /// <summary>
    /// Penalty for the next attack roll. <paramref name="agile"/> uses −4/−8 instead of −5/−10.
    /// The first attack since the pool was full is +0.
    /// </summary>
    public int MapPenalty(EntityUid uid, bool agile, CFActionPoolComponent? pool = null)
    {
        if (!Resolve(uid, ref pool, false))
            return 0;

        return CFMelee.MapPenalty(pool.MapAttacks, agile);
    }

    /// <summary>Counts one attack roll. Misses count. Actions that do not roll an attack do not.</summary>
    public void CountAttack(EntityUid uid, CFActionPoolComponent? pool = null)
    {
        if (!_net.IsServer || !Resolve(uid, ref pool, false))
            return;

        pool.MapAttacks++;
        Dirty(uid, pool);
    }

    /// <summary>Drops one attack off the multiple-attack count. Exacting Strike uses this on a miss.</summary>
    public void UndoAttack(EntityUid uid, CFActionPoolComponent? pool = null)
    {
        if (!_net.IsServer || !Resolve(uid, ref pool, false) || pool.MapAttacks <= 0)
            return;

        pool.MapAttacks--;
        Dirty(uid, pool);
    }

    public int ReadyCount(EntityUid uid, CFActionPoolComponent? pool = null)
    {
        if (!Resolve(uid, ref pool, false))
            return int.MaxValue;

        if (pool.StaminaPerAction <= 0f)
            return pool.MaxActions;

        return (int) MathF.Floor(Math.Max(0f, pool.Stamina) / pool.StaminaPerAction);
    }

    /// <summary>How many actions a radial activity costs. Free activities and reactions cost nothing.</summary>
    public int ActionCount(EntityUid action)
    {
        // A volley costs one action per bolt, paid on the opening click.
        if (TryComp(action, out CFProjectileDeliveryComponent? bolts) && bolts.IsVolley)
            return bolts.PayThisShot ? bolts.ClampedBolts : 0;

        if (!TryComp(action, out CFRadialSlotComponent? slot))
            return 1;

        if (slot.Group is CFRadialGroup.Free or CFRadialGroup.Reaction)
            return 0;

        if (slot.Actions > 0)
            return slot.Actions;

        if (slot.Group == CFRadialGroup.Spell
            && TryComp(action, out CFSpellComponent? spell)
            && _prototypes.TryIndex(spell.Spell, out CFSpellPrototype? proto))
            return Count(proto.ActionCost);

        return slot.Group == CFRadialGroup.Spell ? 2 : 1;
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFActionPoolComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Stamina >= 0f)
            return;

        ent.Comp.Stamina = MaxStamina(ent.Comp);
        Dirty(ent);
    }

    [SubscribeLocalEvent]
    private void OnCombatantMapInit(Entity<CFCombatantComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<MobStateComponent>(ent) || HasComp<CFMaterialComponent>(ent))
            return;

        Ensure(ent);
    }

    [SubscribeLocalEvent]
    private void OnAttackAttempt(Entity<CFActionPoolComponent> ent, ref AttackAttemptEvent args)
    {
        // A check with no target and no weapon is not a swing. Flanking uses that form.
        if (args.Disarm || args.Target == null && args.Weapon == null)
            return;

        if (CanAfford(ent.Comp, 1))
            return;

        args.Cancel();
        PopupEmpty(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnMeleeAttack(Entity<CFActionPoolComponent> ent, ref MeleeAttackEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        TrySpend(ent, ent.Comp, 1);
    }

    [SubscribeLocalEvent]
    private void OnShotAttempt(Entity<CFActionPoolComponent> ent, ref ShotAttemptedEvent args)
    {
        if (CanAfford(ent.Comp, ShotCost(args.Used)))
            return;

        args.Cancel();
        PopupEmpty(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnGunShot(Entity<GunComponent> weapon, ref GunShotEvent args)
    {
        if (!_timing.IsFirstTimePredicted || !TryComp(args.User, out CFActionPoolComponent? pool))
            return;

        TrySpend(args.User, pool, ShotCost(weapon));
    }

    /// <summary>A shot is one Strike action plus the weapon's reload (crossbow 1, heavy crossbow 2).</summary>
    private int ShotCost(EntityUid weapon)
    {
        return 1 + Math.Max(0, CompOrNull<CFRangedWeaponComponent>(weapon)?.Reload ?? 0);
    }

    [SubscribeLocalEvent]
    private void OnActionAttempt(Entity<CFRadialSlotComponent> ent, ref ActionAttemptEvent args)
    {
        if (!TryComp(args.User, out CFActionPoolComponent? pool))
            return;

        var count = ActionCount(ent);
        if (count <= 0 || CanAfford(pool, count))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("cf-action-not-enough");
    }

    [SubscribeLocalEvent]
    private void OnActionPerformed(Entity<CFRadialSlotComponent> ent, ref ActionPerformedEvent args)
    {
        if (!_timing.IsFirstTimePredicted || !TryComp(args.Performer, out CFActionPoolComponent? pool))
            return;

        TrySpend(args.Performer, pool, ActionCount(ent));
    }

    /// <summary>
    /// Spends <paramref name="actions"/> for an activity that is not an action on the wheel (disabling a trap).
    /// A creature without an action pool always can. Shows the usual popup when it cannot afford it.
    /// </summary>
    public bool TrySpendActions(EntityUid uid, int actions)
    {
        if (!TryComp(uid, out CFActionPoolComponent? pool))
            return true;

        if (!CanAfford(pool, actions))
        {
            PopupEmpty(uid, pool);
            return false;
        }

        TrySpend(uid, pool, actions);
        return true;
    }

    private bool CanAfford(CFActionPoolComponent pool, int actions)
    {
        if (actions <= 0)
            return true;

        var cost = pool.StaminaPerAction * actions;
        return cost <= 0f || pool.Stamina + 0.001f >= cost;
    }

    private void TrySpend(EntityUid uid, CFActionPoolComponent pool, int actions)
    {
        if (actions <= 0 || pool.StaminaPerAction <= 0f)
            return;

        var cost = pool.StaminaPerAction * actions;
        if (pool.Stamina + 0.001f < cost)
            return;

        pool.Stamina = Math.Max(0f, pool.Stamina - cost);
        Dirty(uid, pool);
    }

    private void PopupEmpty(EntityUid uid, CFActionPoolComponent pool)
    {
        if (_timing.CurTime < pool.NextEmptyPopup)
            return;

        pool.NextEmptyPopup = _timing.CurTime + TimeSpan.FromSeconds(1.5);
        _popup.PopupEntity(Loc.GetString("cf-action-not-enough"), uid, uid);
    }

    private static int Count(CFSpellActionCost cost)
    {
        return cost switch
        {
            CFSpellActionCost.One => 1,
            CFSpellActionCost.Three => 3,
            CFSpellActionCost.Free => 0,
            CFSpellActionCost.Reaction => 0,
            _ => 2,
        };
    }
}
