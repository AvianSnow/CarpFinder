using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Atmos;
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

        SubscribeLocalEvent<CFFireSpreadComponent, IgnitedEvent>(OnFireStartedSpread);
        SubscribeLocalEvent<CFFireSpreadComponent, ExtinguishedEvent>(OnFireStoppedSpread);
        SubscribeLocalEvent<CFDespawnOnExtinguishComponent, ExtinguishedEvent>(OnFireChangedDespawn);
        SubscribeLocalEvent<CFDelayedIgnitionSourceComponent, IgnitedEvent>(OnIgnitionSourceFireChanged);
        SubscribeLocalEvent<CFDelayedIgnitionSourceComponent, ExtinguishedEvent>(OnIgnitionSourceFireChanged);
        SubscribeLocalEvent<CFDelayedIgnitionSourceComponent, AfterInteractEvent>(OnDelayedIgniteAttempt);
    }

    private void OnFireChangedDespawn(Entity<CFDespawnOnExtinguishComponent> ent, ref ExtinguishedEvent args)
    {
        QueueDel(ent);
    }

    private void OnFireStartedSpread(Entity<CFFireSpreadComponent> ent, ref IgnitedEvent args)
    {
        EnsureComp<CFActiveFireSpreadingComponent>(ent);
        ent.Comp.NextSpreadTime = _gameTiming.CurTime + TimeSpan.FromSeconds(ent.Comp.SpreadCooldownMax);
    }

    private void OnFireStoppedSpread(Entity<CFFireSpreadComponent> ent, ref ExtinguishedEvent args)
    {
        RemCompDeferred<CFActiveFireSpreadingComponent>(ent);
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

    private void OnIgnitionSourceFireChanged(Entity<CFDelayedIgnitionSourceComponent> ent, ref IgnitedEvent args)
    {
        ent.Comp.Enabled = true;
        Dirty(ent);
    }

    private void OnIgnitionSourceFireChanged(Entity<CFDelayedIgnitionSourceComponent> ent, ref ExtinguishedEvent args)
    {
        ent.Comp.Enabled = false;
        Dirty(ent);
    }
}

[Serializable, NetSerializable]
public sealed partial class CFIgnitionDoAfter : SimpleDoAfterEvent
{
}
