using Content.Shared.Chemistry.Components;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Alchemy;

/// <summary>
/// Thrown potions (<see cref="CFSmashPotionComponent"/>) and the puddles they leave
/// (<see cref="CFHealingPuddleComponent"/>). Server only.
/// </summary>
public sealed partial class CFSmashPotionSystem : EntitySystem
{
    private static readonly TimeSpan PuddleLasts = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan DrinkTime = TimeSpan.FromSeconds(2);

    /// <summary>How near a prone creature's centre has to be to the puddle's.</summary>
    private const float RollRange = 0.6f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPuddleSystem _puddles = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFDyingSystem _dying = default!;
    [Dependency] private CFCombatBridgeSystem _bridge = default!;
    [Dependency] private CFVfxSystem _vfx = default!;

    private readonly HashSet<Entity<KnockedDownComponent>> _prone = new();

    [SubscribeLocalEvent]
    private void OnHit(Entity<CFSmashPotionComponent> potion, ref ThrowDoHitEvent args)
    {
        if (!_net.IsServer || potion.Comp.Broken || args.Target == args.Component.Thrower)
            return;

        var target = args.Target;
        if (HasComp<MobStateComponent>(target) && !_mobState.IsDead(target))
        {
            Break(potion);
            if (TryComp(potion, out CFHealConsumableComponent? heal))
            {
                var amount = Heal(target, heal.HealDice, heal.HealDie, heal.HealBonus, args.Component.Thrower);
                _popup.PopupEntity(Loc.GetString("cf-alchemy-smash-heal", ("amount", amount)), target);
            }

            return;
        }

        Spill(potion);
    }

    [SubscribeLocalEvent]
    private void OnLand(Entity<CFSmashPotionComponent> potion, ref LandEvent args)
    {
        if (_net.IsServer && !potion.Comp.Broken)
            Spill(potion);
    }

    private void Break(Entity<CFSmashPotionComponent> potion)
    {
        potion.Comp.Broken = true;
        _audio.PlayPvs(potion.Comp.BreakSound, Transform(potion).Coordinates);
        QueueDel(potion);
    }

    private void Spill(Entity<CFSmashPotionComponent> potion)
    {
        Break(potion);
        var solution = new Solution(potion.Comp.Reagent, FixedPoint2.New(potion.Comp.Units));
        if (!_puddles.TrySpillAt(Transform(potion).Coordinates, solution, out var puddle, sound: false)
            || !TryComp(potion, out CFHealConsumableComponent? heal))
        {
            return;
        }

        // A second potion on the same puddle does not add up: the stronger one counts.
        var now = _timing.CurTime;
        if (TryComp(puddle, out CFHealingPuddleComponent? old) && Average(old) >= Average(heal.HealDice, heal.HealDie, heal.HealBonus))
        {
            old.Expires = now + PuddleLasts;
            return;
        }

        var healing = EnsureComp<CFHealingPuddleComponent>(puddle);
        healing.HealDice = heal.HealDice;
        healing.HealDie = heal.HealDie;
        healing.HealBonus = heal.HealBonus;
        healing.Expires = now + PuddleLasts;
        healing.NextCheck = now + CheckEvery;
    }

    private static float Average(CFHealingPuddleComponent puddle)
    {
        return Average(puddle.HealDice, puddle.HealDie, puddle.HealBonus);
    }

    private static float Average(int dice, int die, int bonus)
    {
        return dice * (die + 1) / 2f + bonus;
    }

    private int Heal(EntityUid target, int dice, int die, int bonus, EntityUid? healer)
    {
        _bridge.TryEnsureCombatTarget(target, out _, out _, out _);
        var amount = bonus;
        for (var i = 0; i < dice; i++)
        {
            amount += _random.Next(1, die + 1);
        }

        amount = Math.Max(0, amount);
        _dying.RestoreHitPoints(target, amount, healer);
        _vfx.Healing(target);
        return amount;
    }

    private void Use(Entity<CFHealingPuddleComponent> puddle, EntityUid target, string message)
    {
        var amount = Heal(target, puddle.Comp.HealDice, puddle.Comp.HealDie, puddle.Comp.HealBonus, target);
        _popup.PopupEntity(Loc.GetString(message, ("amount", amount)), target);
        QueueDel(puddle);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFHealingPuddleComponent>();
        while (query.MoveNext(out var uid, out var puddle))
        {
            if (now < puddle.NextCheck)
                continue;

            puddle.NextCheck = now + CheckEvery;
            if (now >= puddle.Expires)
            {
                RemCompDeferred<CFHealingPuddleComponent>(uid);
                continue;
            }

            _prone.Clear();
            _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(uid), RollRange, _prone);
            foreach (var (creature, _) in _prone)
            {
                if (!CanBeHealed(creature))
                    continue;

                Use((uid, puddle), creature, "cf-alchemy-puddle-roll");
                break;
            }
        }
    }

    /// <summary>Alive and hurt, so nobody wastes the puddle by lying in it at full health.</summary>
    private bool CanBeHealed(EntityUid creature)
    {
        if (!HasComp<MobStateComponent>(creature) || _mobState.IsDead(creature))
            return false;

        return !TryComp(creature, out CFCombatantComponent? combat) || combat.CurrentHp < combat.MaxHp;
    }

    [SubscribeLocalEvent]
    private void OnVerbs(Entity<CFHealingPuddleComponent> puddle, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<MobStateComponent>(args.User))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cf-alchemy-puddle-drink-verb"),
            Priority = 2,
            Act = () =>
            {
                _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, DrinkTime, new CFDrinkPuddleDoAfterEvent(), puddle, target: puddle)
                {
                    BreakOnMove = true,
                    BreakOnDamage = true,
                    CancelDuplicate = true,
                });
            },
        });
    }

    [SubscribeLocalEvent]
    private void OnDrink(Entity<CFHealingPuddleComponent> puddle, ref CFDrinkPuddleDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !_net.IsServer || TerminatingOrDeleted(puddle))
            return;

        args.Handled = true;
        Use(puddle, args.User, "cf-alchemy-puddle-drink");
    }
}
