using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Effects;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// A monster's ranged attack, as an action: a spider's web, a spit, a thrown rock. What it does is the effect
/// pipeline's data on the same action: <c>CFTargeting</c> (range, and <c>delivery: Projectile</c> with a
/// <c>CFProjectileDelivery</c> to draw it flying), <c>CFCheck</c> with <c>defense: ArmorClass</c> and
/// <c>dcSource: Fixed</c> (the attack bonus is <c>fixedDc</c> less 10), and <c>CFDegreeEffects</c>, which can deal
/// damage or none at all. Give it to the monster with <c>ActionGrant</c>. A player in the body aims it from the
/// hotbar, and the AI uses it through its Breathe step.
/// </summary>
[RegisterComponent]
public sealed partial class CFMonsterShotComponent : Component
{
    /// <summary>The AI does not use it on a target closer than this, where its melee Strikes are better.</summary>
    [DataField]
    public float MinFeet = 10f;

    /// <summary>The AI does not use it on a creature that is already grabbed or stuck, as a second web adds nothing.</summary>
    [DataField]
    public bool SkipHeld = true;
}

public sealed partial class CFMonsterShotEvent : EntityTargetActionEvent
{
}

/// <summary>
/// On something that holds a creature where it is: a web. The creature counts as grabbed by it, without being
/// off-guard, and gets out with Escape against <see cref="EscapeDc"/>. The holder is deleted when its creature gets
/// free, and lets go if it is destroyed first.
/// </summary>
[RegisterComponent]
public sealed partial class CFStickyHoldComponent : Component
{
    [DataField]
    public int EscapeDc = 15;

    [DataField]
    public EntityUid? Held;
}

/// <summary>
/// Sticks the target where it stands until it Escapes: immobilized, and held by a spawned <see cref="Holder"/>, such
/// as a web. A critical hit does the same; the rules give a web no more for one.
/// </summary>
public sealed partial class CFStickEffect : CFEffect
{
    [DataField(required: true)]
    public EntProtoId Holder;

    [DataField(required: true)]
    public int EscapeDc;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFMonsterShotSystem>().Stick(target, Holder, EscapeDc);
    }
}

public sealed partial class CFMonsterShotSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFActionPoolSystem _pool = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;

    [SubscribeLocalEvent]
    private void OnShot(Entity<CFMonsterShotComponent> ent, ref CFMonsterShotEvent args)
    {
        if (args.Handled)
            return;

        if (!InRange(args.Performer, ent, args.Target) || !_effects.CanTarget(ent, args.Performer, args.Target))
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("cf-monster-shot-out-of-range"), args.Performer, args.Performer);

            return;
        }

        // Handled, so the action pays its actions.
        args.Handled = true;
        if (_net.IsServer)
            _effects.DeliverToTarget(ent, args.Performer, args.Target);
    }

    /// <summary>The first ranged attack the creature has that it can pay for now.</summary>
    public bool TryGetReady(EntityUid user, out Entity<ActionComponent> shot)
    {
        foreach (var action in _actions.GetActions(user))
        {
            if (!HasComp<CFMonsterShotComponent>(action)
                || _actions.IsCooldownActive(action.Comp)
                || !_pool.CanAffordActions(user, _pool.ActionCount(action)))
            {
                continue;
            }

            shot = action;
            return true;
        }

        shot = default;
        return false;
    }

    public bool InRange(EntityUid user, EntityUid shot, EntityUid target)
    {
        var feet = CompOrNull<CFTargetingComponent>(shot)?.RangeFeet ?? 0;
        var from = _transform.GetMapCoordinates(user);
        var to = _transform.GetMapCoordinates(target);
        return feet > 0
               && from.MapId == to.MapId
               && (to.Position - from.Position).Length() * CFMelee.FeetPerTile <= feet;
    }

    /// <summary>Whether the AI should use this ranged attack on the target now: in range, not too close, and worth it.</summary>
    public bool Worthwhile(EntityUid user, EntityUid shot, EntityUid target)
    {
        if (!TryComp(shot, out CFMonsterShotComponent? comp) || !_mobState.IsAlive(target) || !InRange(user, shot, target))
            return false;

        var distance = (_transform.GetWorldPosition(target) - _transform.GetWorldPosition(user)).Length() * CFMelee.FeetPerTile;
        if (distance < comp.MinFeet)
            return false;

        return !comp.SkipHeld || CompOrNull<CFConditionComponent>(target)?.Grabbed != true;
    }

    /// <summary>Uses the ranged attack at the target, through the same action a player would click.</summary>
    public bool TryShoot(EntityUid user, Entity<ActionComponent> shot, EntityUid target)
    {
        var ev = new CFMonsterShotEvent { Target = target };
        _actions.PerformAction(user, shot, ev);
        return ev.Handled;
    }

    /// <summary>Holds the target where it stands until it Escapes against the DC.</summary>
    public void Stick(EntityUid target, EntProtoId holder, int escapeDc)
    {
        if (!_net.IsServer || !_mobState.IsAlive(target))
            return;

        // One web is enough: a creature already held stays held by what has it.
        if (CompOrNull<CFConditionComponent>(target)?.Grabbed == true)
            return;

        var uid = Spawn(holder, _transform.GetMapCoordinates(target));
        var hold = EnsureComp<CFStickyHoldComponent>(uid);
        hold.EscapeDc = escapeDc;
        hold.Held = target;
        _conditions.ApplyGrabbed(target, uid);

        // A reaction can slip a grab as it lands.
        if (CompOrNull<CFConditionComponent>(target)?.Grabber != uid)
            QueueDel(uid);
    }

    /// <summary>The holder is destroyed, burnt or cleared away: what it held is free.</summary>
    [SubscribeLocalEvent]
    private void OnHolderGone(Entity<CFStickyHoldComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Held is { } held
            && !TerminatingOrDeleted(held)
            && CompOrNull<CFConditionComponent>(held)?.Grabber == ent.Owner)
        {
            ent.Comp.Held = null;
            _conditions.ReleaseGrab(held);
        }
    }
}
