// Ported from Starlight (https://github.com/ss14Starlight/space-station-14), their Scent system, with permission.
// Cut down for CarpFinder: no traces on objects, no sneezing, no cleaning, no pressure or vent crawling.

using Content.Shared.CF.Scent;
using Content.Shared.Eye;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server.CF.Scent;

/// <summary>Lays scent trails: every <see cref="CFScentComponent"/> leaves marks as it moves, and a sniffing smeller can see them.</summary>
public sealed partial class CFScentServerSystem : CFSharedScentSystem
{
    private static readonly EntProtoId MarkerPrototype = "CFScentMarker";
    private const int ScentIdByteLength = 8;

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CFScentComponent, MapInitEvent>(OnScentMapInit);
        SubscribeLocalEvent<CFSmellerComponent, GetVisMaskEvent>(OnGetVisMask);
    }

    private void OnScentMapInit(Entity<CFScentComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.ScentId != null)
            return;

        var bytes = new byte[ScentIdByteLength];
        _random.NextBytes(bytes);
        ent.Comp.ScentId = Convert.ToHexString(bytes);
        Dirty(ent);
    }

    private void OnGetVisMask(Entity<CFSmellerComponent> ent, ref GetVisMaskEvent args)
    {
        if (ent.Comp.Sniffing)
            args.VisibilityMask |= CFScentVisibility.Layer;
    }

    /// <summary>Which entities a client is sent depends on the mask, so this cannot be predicted.</summary>
    public override void SetSniffing(Entity<CFSmellerComponent> ent, bool sniffing)
    {
        base.SetSniffing(ent, sniffing);
        _eye.RefreshVisibilityMask(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFScentComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scent, out var xform))
        {
            if (scent.ScentId is not { } scentId)
                continue;

            if (scent.NextEmitTime == TimeSpan.Zero)
                scent.NextEmitTime = now + RollEmitDelay(scent);

            if (now < scent.NextEmitTime)
                continue;

            scent.NextEmitTime = now + RollEmitDelay(scent);
            if (xform.GridUid == null)
                continue;

            Emit((uid, scent, xform), scentId);
        }
    }

    private TimeSpan RollEmitDelay(CFScentComponent scent)
    {
        var variance = scent.EmitInterval * scent.EmitIntervalVariance;
        var seconds = Math.Max(scent.MinEmitInterval, scent.EmitInterval + _random.NextFloat(-variance, variance));
        return TimeSpan.FromSeconds(seconds);
    }

    private void Emit(Entity<CFScentComponent, TransformComponent> ent, string scentId)
    {
        var (uid, scent, xform) = ent;
        var decay = TimeSpan.FromSeconds(scent.DecayTime);

        // Standing still pools the scent on the newest mark instead of piling marks up.
        if (scent.LastMarkerEntity is { } tail
            && TryComp(tail, out CFScentMarkerComponent? marker)
            && TryComp(tail, out TransformComponent? tailXform)
            && _transform.InRange(xform.Coordinates, tailXform.Coordinates, scent.MergeRadius))
        {
            marker.Strength = Math.Min(1f, marker.Strength + scent.MergeStrengthStep);
            marker.ExpiresAt = _timing.CurTime + decay;
            marker.TotalDuration = decay;
            Dirty(tail, marker);
            if (TryComp(tail, out TimedDespawnComponent? tailDespawn))
                tailDespawn.Lifetime = (float) decay.TotalSeconds;

            return;
        }

        var mark = SpawnAtPosition(MarkerPrototype, xform.Coordinates);
        var comp = Comp<CFScentMarkerComponent>(mark);
        comp.ScentId = scentId;
        comp.ExpiresAt = _timing.CurTime + decay;
        comp.TotalDuration = decay;
        Dirty(mark, comp);
        Comp<TimedDespawnComponent>(mark).Lifetime = (float) decay.TotalSeconds;
        scent.LastMarkerEntity = mark;
    }
}
