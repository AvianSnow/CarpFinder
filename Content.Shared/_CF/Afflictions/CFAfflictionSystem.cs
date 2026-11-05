using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Time;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Afflictions;

/// <summary>
/// Runs staged afflictions (GM Core). A failed exposure save starts the onset. After it, each stage ends in a save:
/// a success lowers the stage by 1, a critical success by 2, a failure raises it by 1, and a critical failure by 2.
/// A new exposure while afflicted raises the stage on a failed save, and leaves the timer alone.
/// </summary>
public sealed partial class CFAfflictionSystem : EntitySystem
{
    /// <summary>World seconds per real second when no world clock exists, such as in tests.</summary>
    private const float DefaultWorldRate = 4f;

    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFAfflictionsComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_mobState.IsDead(uid))
            {
                RemCompDeferred<CFAfflictionsComponent>(uid);
                continue;
            }

            for (var i = comp.Active.Count - 1; i >= 0; i--)
            {
                var active = comp.Active[i];
                if (now >= active.NextTick && !Tick(uid, active, now))
                    comp.Active.RemoveAt(i);
            }

            if (comp.Active.Count == 0)
                RemCompDeferred<CFAfflictionsComponent>(uid);
        }
    }

    /// <summary>The stage of an affliction on this creature. Zero during the onset, null if it has none.</summary>
    public int? Stage(EntityUid uid, ProtoId<CFAfflictionPrototype> id)
    {
        return TryComp(uid, out CFAfflictionsComponent? comp) && Find(comp, id) is { } active
            ? active.Stage
            : null;
    }

    /// <summary>Exposes a creature, rolling the save. Does nothing during the affliction's cooldown.</summary>
    public void Expose(EntityUid uid, ProtoId<CFAfflictionPrototype> id)
    {
        if (!_net.IsServer || !_prototypes.Resolve(id, out var proto) || _mobState.IsDead(uid))
            return;

        var comp = EnsureComp<CFAfflictionsComponent>(uid);
        var active = Find(comp, id);
        var now = _timing.CurTime;
        if (active != null && now < active.NextExposure)
            return;

        var degree = _conditions.RollDefense(uid, proto.Defense, proto.Dc, out _);
        var cooldown = now + TimeSpan.FromSeconds(proto.ExposureCooldownSeconds);
        if (active != null)
            active.NextExposure = cooldown;

        if (degree is CFDegree.Success or CFDegree.CriticalSuccess)
            return;

        var raise = degree == CFDegree.CriticalFailure ? 2 : 1;
        if (active == null)
        {
            comp.Active.Add(new CFActiveAffliction
            {
                Id = id,
                PendingStage = raise,
                NextTick = now + WorldTime(proto.OnsetWorldMinutes),
                NextExposure = cooldown,
            });
            return;
        }

        if (active.Stage == 0)
        {
            active.PendingStage = Math.Min(active.PendingStage + raise, proto.Stages.Count);
            return;
        }

        // A new exposure does not change how long the current stage lasts.
        var next = active.NextTick;
        EnterStage(uid, proto, active, active.Stage + raise);
        active.NextTick = next;
    }

    /// <summary>Handles a due onset or save. False when the affliction is over.</summary>
    private bool Tick(EntityUid uid, CFActiveAffliction active, TimeSpan now)
    {
        if (!_prototypes.Resolve(active.Id, out var proto))
            return false;

        if (active.Stage == 0)
            return EnterStage(uid, proto, active, active.PendingStage);

        var change = _conditions.RollDefense(uid, proto.Defense, proto.Dc, out _) switch
        {
            CFDegree.CriticalSuccess => -2,
            CFDegree.Success => -1,
            CFDegree.CriticalFailure when proto.FailureRaisesStage => 2,
            CFDegree.Failure when proto.FailureRaisesStage => 1,
            _ => 0,
        };

        var stage = active.Stage + change;
        if (stage <= 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-affliction-recovered"), uid, uid);
            return false;
        }

        return EnterStage(uid, proto, active, stage);
    }

    private bool EnterStage(EntityUid uid, CFAfflictionPrototype proto, CFActiveAffliction active, int stage)
    {
        stage = Math.Clamp(stage, 1, proto.Stages.Count);
        active.Stage = stage;

        var data = proto.Stages[stage - 1];
        active.NextTick = _timing.CurTime + WorldTime(data.WorldMinutes);
        _effects.Run(data.Effects, new CFEffectArgs { User = uid, Source = uid, Target = uid, Dc = proto.Dc });

        // A stage with no time on it is the end of the track, such as death.
        return data.WorldMinutes > 0f;
    }

    private TimeSpan WorldTime(float worldMinutes)
    {
        var query = EntityQueryEnumerator<CFTimeComponent>();
        if (query.MoveNext(out var clock)
            && clock.TryGetRealSeconds(worldMinutes * 60f, CFTimeScale.CarpFinder, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(worldMinutes * 60f / DefaultWorldRate);
    }

    private static CFActiveAffliction? Find(CFAfflictionsComponent comp, ProtoId<CFAfflictionPrototype> id)
    {
        foreach (var active in comp.Active)
        {
            if (active.Id == id)
                return active;
        }

        return null;
    }
}

/// <summary>Reagent effect: exposes the creature to an affliction.</summary>
public sealed partial class CFExposeAffliction : EntityEffectBase<CFExposeAffliction>
{
    [DataField(required: true)]
    public ProtoId<CFAfflictionPrototype> Affliction;
}

/// <inheritdoc cref="EntityEffectSystem{T, TEffect}"/>
public sealed partial class CFExposeAfflictionEffectSystem : EntityEffectSystem<MetaDataComponent, CFExposeAffliction>
{
    [Dependency] private CFAfflictionSystem _afflictions = default!;

    protected override void Effect(Entity<MetaDataComponent> entity, ref EntityEffectEvent<CFExposeAffliction> args)
    {
        _afflictions.Expose(entity, args.Effect.Affliction);
    }
}
