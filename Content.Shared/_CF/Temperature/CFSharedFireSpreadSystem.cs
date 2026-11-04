using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Temperature;

public abstract partial class CFSharedFireSpreadSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CFFireSpreadComponent, OnFireChangedEvent>(OnFireChangedSpread);
        SubscribeLocalEvent<CFDespawnOnExtinguishComponent, OnFireChangedEvent>(OnFireChangedDespawn);
        SubscribeLocalEvent<CFDelayedIgnitionSourceComponent, OnFireChangedEvent>(OnIgnitionSourceFireChanged);
        SubscribeLocalEvent<CFDelayedIgnitionSourceComponent, AfterInteractEvent>(OnDelayedIgniteAttempt);
    }

    private void OnFireChangedDespawn(Entity<CFDespawnOnExtinguishComponent> ent, ref OnFireChangedEvent args)
    {
        if (!args.OnFire)
            QueueDel(ent);
    }

    private void OnFireChangedSpread(Entity<CFFireSpreadComponent> ent, ref OnFireChangedEvent args)
    {
        if (args.OnFire)
        {
            EnsureComp<CFActiveFireSpreadingComponent>(ent);
        }
        else
        {
            if (HasComp<CFActiveFireSpreadingComponent>(ent))
                RemCompDeferred<CFActiveFireSpreadingComponent>(ent);
        }

        ent.Comp.NextSpreadTime = _gameTiming.CurTime + TimeSpan.FromSeconds(ent.Comp.SpreadCooldownMax);
    }

    private void OnDelayedIgniteAttempt(Entity<CFDelayedIgnitionSourceComponent> ent, ref AfterInteractEvent args)
    {
        if (!args.CanReach || args.Handled || args.Target == null)
            return;

        if (!ent.Comp.Enabled)
            return;

        var time = ent.Comp.Delay;
        var caution = true;

        if (TryComp<CFIgnitionModifierComponent>(args.Target, out var modifier))
        {
            time *= modifier.IgnitionTimeModifier;
            caution = !modifier.HideCaution;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            args.User,
            time,
            new CFIgnitionDoAfter(),
            args.Target,
            args.Target,
            ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            BreakOnDropItem = true,
            BreakOnHandChange = true,
            BlockDuplicate = true,
            CancelDuplicate = true
        });

        var selfMessage = Loc.GetString("cf-attempt-ignite-caution-self",
            ("target", MetaData(args.Target.Value).EntityName));
        var otherMessage = Loc.GetString("cf-attempt-ignite-caution",
            ("name", Identity.Entity(args.User, EntityManager)),
            ("target", Identity.Entity(args.Target.Value, EntityManager)));
        _popup.PopupPredicted(selfMessage,
            otherMessage,
            args.User,
            args.User,
            caution ? PopupType.MediumCaution : PopupType.Small);
    }

    private void OnIgnitionSourceFireChanged(Entity<CFDelayedIgnitionSourceComponent> ent, ref OnFireChangedEvent args)
    {
        ent.Comp.Enabled = args.OnFire;
        Dirty(ent);
    }
}

/// <summary>
/// Raised whenever an FlammableComponent OnFire is Changed
/// </summary>
[ByRefEvent]
public readonly record struct OnFireChangedEvent(bool OnFire)
{
    public readonly bool OnFire = OnFire;
}

[Serializable, NetSerializable]
public sealed partial class CFIgnitionDoAfter : SimpleDoAfterEvent
{
}
