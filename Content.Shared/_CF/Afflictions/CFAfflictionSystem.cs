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
                comp.Active.Clear();
                RefreshMarkers(uid, comp);
                RemCompDeferred<CFAfflictionsComponent>(uid);
                continue;
            }

            for (var i = comp.Active.Count - 1; i >= 0; i--)
            {
                var active = comp.Active[i];
                var over = active.EndsAt > TimeSpan.Zero && now >= active.EndsAt;
                if (over)
                    _popup.PopupEntity(Loc.GetString("cf-affliction-recovered"), uid, uid);

                if (over || now >= active.NextTick && !Tick(uid, active, now))
                    comp.Active.RemoveAt(i);
            }

            // Cheap: it writes nothing unless the worst stage changed.
            RefreshMarkers(uid, comp);

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

        // Immune to poison, or to disease: it never takes hold.
        if (_conditions.IsImmuneToTraits(uid, proto.Traits))
            return;

        var comp = EnsureComp<CFAfflictionsComponent>(uid);
        var active = Find(comp, id);
        var now = _timing.CurTime;
        if (active != null && now < active.NextExposure)
            return;

        var degree = _conditions.RollDefense(uid, proto.Defense, proto.Dc, out _, proto.Traits);
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
                NextTick = now + (proto.OnsetSeconds > 0f ? TimeSpan.FromSeconds(proto.OnsetSeconds) : WorldTime(proto.OnsetWorldMinutes)),
                NextExposure = cooldown,
                EndsAt = proto.MaxSeconds > 0f ? now + TimeSpan.FromSeconds(proto.MaxSeconds) : TimeSpan.Zero,
            });
            _popup.PopupEntity(Loc.GetString(Marker(proto) == CFConditionKind.Diseased ? "cf-affliction-diseased" : "cf-affliction-poisoned"), uid, uid);
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

        // Strong-Blooded Dwarf: a successful save against a poison lowers its stage by one more.
        var strong = proto.Traits.Contains("Poison") && CompOrNull<CFCombatOptionsComponent>(uid) is { StrongBlooded: true } ? 1 : 0;
        var change = _conditions.RollDefense(uid, proto.Defense, proto.Dc, out _, proto.Traits) switch
        {
            CFDegree.CriticalSuccess => -2 - strong,
            CFDegree.Success => -1 - strong,
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
        active.NextTick = _timing.CurTime + (data.Seconds > 0f ? TimeSpan.FromSeconds(data.Seconds) : WorldTime(data.WorldMinutes));
        _effects.Run(data.Effects, new CFEffectArgs { User = uid, Source = uid, Target = uid, Position = Transform(uid).Coordinates, Dc = proto.Dc, Traits = proto.Traits });

        // A stage with no time on it is the end of the track, such as death.
        return data.Seconds > 0f || data.WorldMinutes > 0f;
    }

    /// <summary>Whether a poison (or a disease) is running on the creature, onset included.</summary>
    public bool Has(EntityUid uid, CFConditionKind marker)
    {
        if (!TryComp(uid, out CFAfflictionsComponent? comp))
            return false;

        foreach (var active in comp.Active)
        {
            if (_prototypes.Resolve(active.Id, out var proto) && Marker(proto) == marker)
                return true;
        }

        return false;
    }

    /// <summary>Ends every poison (or every disease) on the creature, as a cure does. Returns how many ended.</summary>
    public int Cure(EntityUid uid, CFConditionKind marker)
    {
        if (!TryComp(uid, out CFAfflictionsComponent? comp))
            return 0;

        var cured = comp.Active.RemoveAll(active => _prototypes.Resolve(active.Id, out var proto) && Marker(proto) == marker);
        if (cured > 0)
            RefreshMarkers(uid, comp);

        return cured;
    }

    /// <summary>Poisoned or diseased, by the affliction's trait. Anything else, such as drink, has no marker.</summary>
    private static CFConditionKind? Marker(CFAfflictionPrototype proto)
    {
        if (proto.Traits.Contains("Poison"))
            return CFConditionKind.Poisoned;

        return proto.Traits.Contains("Disease") ? CFConditionKind.Diseased : null;
    }

    /// <summary>Keeps the Poisoned and Diseased conditions, which the HUD shows, at the worst stage running.</summary>
    private void RefreshMarkers(EntityUid uid, CFAfflictionsComponent comp)
    {
        var poison = 0;
        var disease = 0;
        foreach (var active in comp.Active)
        {
            if (!_prototypes.Resolve(active.Id, out var proto))
                continue;

            // An onset counts as stage 1 for the marker: the creature knows it has been bitten.
            var stage = Math.Max(1, active.Stage);
            switch (Marker(proto))
            {
                case CFConditionKind.Poisoned:
                    poison = Math.Max(poison, stage);
                    break;
                case CFConditionKind.Diseased:
                    disease = Math.Max(disease, stage);
                    break;
            }
        }

        _conditions.SetMarker(uid, CFConditionKind.Poisoned, poison);
        _conditions.SetMarker(uid, CFConditionKind.Diseased, disease);
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

/// <summary>Exposes the target to an affliction, which rolls its save: a venomous bite, a tainted drink.</summary>
public sealed partial class CFExposeAfflictionEffect : CFEffect
{
    [DataField(required: true)]
    public ProtoId<CFAfflictionPrototype> Affliction;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFAfflictionSystem>().Expose(target, Affliction);
    }
}

/// <summary>Ends every poison or every disease on the target (<c>kind</c>: Poisoned or Diseased).</summary>
public sealed partial class CFCureAfflictionEffect : CFEffect
{
    [DataField(required: true)]
    public CFConditionKind Kind;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is { } target)
            entities.System<CFAfflictionSystem>().Cure(target, Kind);
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
