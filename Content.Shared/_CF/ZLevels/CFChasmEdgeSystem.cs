using System.Numerics;
using Content.Shared.Chasm;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.GOAP;
using Content.Shared.Gravity;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.ZLevels;

/// <summary>
/// Chasm edges for creatures. Stock drops anything into a chasm the moment its body touches one: its step trigger
/// measures world bounds, which take in the 2.5 tile flanking sensor, so a single pixel of contact counts as standing
/// on it. Here a creature only slips once its centre is over the hole. It then floats weightless for
/// <see cref="CFChasmEdgeComponent.Grace"/> and can steer back onto solid ground. If it is still over the hole after
/// that, it spends its reaction on Grab an Edge, a Reflex save: on a success it is pulled onto solid ground within
/// <see cref="CFChasmEdgeComponent.LeapFeet"/>, and on a failure it falls as stock does.
/// </summary>
public sealed partial class CFChasmEdgeSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> CatwalkTag = "Catwalk";

    /// <summary>How close a chasm has to be to the body for the creature to still count as at the edge.</summary>
    private const float EdgeMargin = 0.1f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ChasmSystem _chasm = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private CFConditionSystem _conditions = default!;

    [Dependency] private EntityQuery<ChasmComponent> _chasmQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;

    [SubscribeLocalEvent]
    private void OnFallAttempt(Entity<CFChasmEdgeComponent> ent, ref EntityStartFallingAttemptEvent args)
    {
        if (!HasComp<MobStateComponent>(args.Faller))
            return;

        // Creatures fall by where their centre is, and only after a chance to get back. Cancelled on the client
        // too, or it would predict the fall.
        args.Cancelled = true;
        if (!_net.IsServer)
            return;

        var slip = EnsureComp<CFChasmSlipComponent>(args.Faller);
        if (!slip.Slipping)
            slip.Chasm = ent;
    }

    [SubscribeLocalEvent]
    private void OnIsWeightless(Entity<CFChasmSlipComponent> ent, ref IsWeightlessEvent args)
    {
        if (!ent.Comp.Slipping || ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.IsWeightless = true;
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnSlipState(Entity<CFChasmSlipComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _gravity.RefreshWeightless(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnSlipShutdown(Entity<CFChasmSlipComponent> ent, ref ComponentShutdown args)
    {
        _gravity.RefreshWeightless(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFChasmSlipComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var slip, out var xform, out var body))
        {
            if (HasComp<ChasmFallingComponent>(uid)
                || xform.GridUid is not { } gridUid
                || !_gridQuery.TryComp(gridUid, out var grid))
            {
                Release((uid, slip));
                continue;
            }

            var tile = _map.CoordinatesToTile(gridUid, grid, _transform.GetMapCoordinates(uid, xform));
            if (ChasmAt(gridUid, grid, tile) is not { } chasm)
            {
                if (slip.Slipping)
                {
                    SetSlipping((uid, slip), false);
                    _popup.PopupEntity(Loc.GetString("cf-chasm-footing"), uid, uid);
                }

                if (!AtEdge(uid, xform, gridUid, grid))
                    Release((uid, slip));

                continue;
            }

            slip.Chasm = chasm;
            if (!slip.Slipping)
            {
                // A leap or a throw carries it over. It slips where it comes down.
                // Something else that makes it weightless, such as no gravity, keeps it from falling at all.
                if (body.BodyStatus == BodyStatus.InAir || _gravity.IsWeightless(uid))
                    continue;

                StartSlipping((uid, slip), body, chasm, now);
                continue;
            }

            if (now >= slip.SlipEnds)
                GrabAnEdge((uid, slip), xform, gridUid, grid);
        }
    }

    private void StartSlipping(Entity<CFChasmSlipComponent> ent, PhysicsComponent body, EntityUid chasm, TimeSpan now)
    {
        var edge = CompOrNull<CFChasmEdgeComponent>(chasm);
        ent.Comp.SlipEnds = now + (edge?.Grace ?? TimeSpan.FromSeconds(4));
        ent.Comp.GrabDc = edge?.GrabDc ?? 10;
        ent.Comp.LeapFeet = edge?.LeapFeet ?? 10;

        // It catches itself, so it does not sail on into the hole with the speed it walked in at.
        _physics.SetLinearVelocity(ent, Vector2.Zero, body: body);
        SetSlipping(ent, true);
        _popup.PopupEntity(Loc.GetString("cf-chasm-falling"), ent, ent, PopupType.LargeCaution);
    }

    /// <summary>
    /// Grab an Edge. It needs the creature awake with its reaction, and solid ground in reach to be pulled onto.
    /// </summary>
    private void GrabAnEdge(Entity<CFChasmSlipComponent> ent, TransformComponent xform, EntityUid gridUid, MapGridComponent grid)
    {
        var uid = ent.Owner;
        var chasm = ent.Comp.Chasm;
        var dc = ent.Comp.GrabDc;
        SetSlipping(ent, false);

        if (!_mobState.IsIncapacitated(uid)
            && FindLedge(uid, xform, gridUid, grid, ent.Comp.LeapFeet) is { } ledge)
        {
            if (TrySpendReaction(uid))
            {
                var degree = _conditions.RollDefense(uid, CFDefenseType.Reflex, dc, out var total);
                if (degree >= CFDegree.Success)
                {
                    _transform.SetCoordinates(uid, ledge);
                    if (TryComp(uid, out PhysicsComponent? body))
                        _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);

                    _popup.PopupEntity(
                        Loc.GetString("cf-chasm-grab", ("total", total), ("dc", dc)),
                        Loc.GetString("cf-chasm-grab-others", ("name", Identity.Entity(uid, EntityManager))),
                        uid,
                        uid,
                        PopupType.Medium);
                    Release(ent);
                    return;
                }

                _popup.PopupEntity(Loc.GetString("cf-chasm-grab-failed", ("total", total), ("dc", dc)), uid, uid, PopupType.MediumCaution);
            }
            else
            {
                _popup.PopupEntity(Loc.GetString("cf-chasm-no-reaction"), uid, uid, PopupType.MediumCaution);
            }
        }

        Release(ent);
        Fall(uid, chasm);
    }

    /// <summary>Stock's fall. Its sound is predicted for the faller, which nothing predicts here, so it is played for all.</summary>
    private void Fall(EntityUid uid, EntityUid chasm)
    {
        if (!_chasmQuery.TryComp(chasm, out var comp))
            return;

        _chasm.StartFalling((chasm, comp), uid, playSound: false);
        _audio.PlayPvs(comp.FallingSound, chasm);
    }

    /// <summary>Grab an Edge is a reaction. A creature that does not track reactions always has one.</summary>
    private bool TrySpendReaction(EntityUid uid)
    {
        return !HasComp<CFCombatOptionsComponent>(uid) || _conditions.TrySpendReaction(uid, CFReactions.Edge);
    }

    private void SetSlipping(Entity<CFChasmSlipComponent> ent, bool slipping)
    {
        if (ent.Comp.Slipping == slipping)
            return;

        ent.Comp.Slipping = slipping;
        Dirty(ent);
        _gravity.RefreshWeightless(ent.Owner);
    }

    private void Release(Entity<CFChasmSlipComponent> ent)
    {
        SetSlipping(ent, false);
        RemCompDeferred<CFChasmSlipComponent>(ent);
    }

    /// <summary>The chasm on a tile, unless a catwalk covers it.</summary>
    private EntityUid? ChasmAt(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        EntityUid? found = null;
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var other))
        {
            if (_chasmQuery.HasComp(other.Value))
                found = other.Value;
            else if (_tags.HasTag(other.Value, CatwalkTag))
                return null;
        }

        return found;
    }

    /// <summary>Whether any part of the creature's body is over a chasm, give or take <see cref="EdgeMargin"/>.</summary>
    private bool AtEdge(EntityUid uid, TransformComponent xform, EntityUid gridUid, MapGridComponent grid)
    {
        var local = Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(gridUid));
        var box = Box2.CenteredAround(local, BodySize(uid, xform) + new Vector2(EdgeMargin * 2));
        foreach (var tile in _map.GetLocalTilesIntersecting(gridUid, grid, box, ignoreEmpty: false))
        {
            if (ChasmAt(gridUid, grid, tile.GridIndices) != null)
                return true;
        }

        return false;
    }

    /// <summary>The size of the solid part of the body. The flanking sensor and other sensors are left out.</summary>
    private Vector2 BodySize(EntityUid uid, TransformComponent xform)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixtures))
            return new Vector2(0.7f);

        var transform = _physics.GetPhysicsTransform(uid, xform);
        Box2? bounds = null;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            for (var i = 0; i < fixture.Shape.ChildCount; i++)
            {
                var aabb = fixture.Shape.ComputeAABB(transform, i);
                bounds = bounds?.Union(aabb) ?? aabb;
            }
        }

        return bounds?.Size ?? new Vector2(0.7f);
    }

    /// <summary>
    /// The nearest tile within <paramref name="feet"/> that is solid floor, not a chasm or lava, not walled up, and
    /// with nothing in the way.
    /// </summary>
    private EntityCoordinates? FindLedge(EntityUid uid, TransformComponent xform, EntityUid gridUid, MapGridComponent grid, float feet)
    {
        var origin = _transform.GetMapCoordinates(uid, xform);
        var local = Vector2.Transform(origin.Position, _transform.GetInvWorldMatrix(gridUid));
        var range = feet / 5f;

        EntityCoordinates? best = null;
        var bestDistance = float.MaxValue;
        foreach (var tile in _map.GetLocalTilesIntersecting(gridUid, grid, new Circle(local, range + 0.5f)))
        {
            var centre = _map.GridTileToLocal(gridUid, grid, tile.GridIndices);
            var distance = (centre.Position - local).Length();
            if (distance > range + 0.5f || distance >= bestDistance || !Solid(gridUid, grid, tile.GridIndices))
                continue;

            if (!_interaction.InRangeUnobstructed(origin, _transform.ToMapCoordinates(centre), range + 1f,
                    CollisionGroup.Impassable, e => e == uid))
            {
                continue;
            }

            best = centre;
            bestDistance = distance;
        }

        return best;
    }

    private bool Solid(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        var catwalk = false;
        var chasm = false;
        while (anchored.MoveNext(out var other))
        {
            if (_tags.HasTag(other.Value, CatwalkTag))
                catwalk = true;
            else if (_chasmQuery.HasComp(other.Value))
                chasm = true;
            else if (HasComp<CFDangerousTileComponent>(other.Value))
                return false;
            else if (TryComp(other.Value, out PhysicsComponent? body) && body.CanCollide && body.Hard
                     && (body.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
                return false;
        }

        return catwalk || !chasm;
    }
}
