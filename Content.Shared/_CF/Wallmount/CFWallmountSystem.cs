// Ported from CrystallPunk 14 (CP14WallmountSystem).
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Wallmount;

public sealed partial class CFWallmountSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    public static readonly ProtoId<TagPrototype>[] WallTags = { "Wall", "Window" };

    [SubscribeLocalEvent]
    private void OnWallAnchorChanged(Entity<CFWallmountedComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            ClearWallmounts(ent);
    }

    [SubscribeLocalEvent]
    private void OnWallShutdown(Entity<CFWallmountedComponent> ent, ref ComponentShutdown args)
    {
        ClearWallmounts(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<CFWallmountComponent>();
        while (query.MoveNext(out var uid, out var wallmount))
        {
            if (_timing.CurTime < wallmount.NextAttachTime)
                continue;

            if (wallmount.AttachAttempts <= 0)
            {
                QueueDel(uid);
                continue;
            }

            wallmount.NextAttachTime = _timing.CurTime + TimeSpan.FromSeconds(0.5f);
            wallmount.AttachAttempts--;

            if (TryAttach((uid, wallmount)))
                RemComp<CFWallmountComponent>(uid);
        }
    }

    private void ClearWallmounts(Entity<CFWallmountedComponent> ent)
    {
        foreach (var attached in ent.Comp.Attached)
        {
            QueueDel(attached);
        }

        ent.Comp.Attached.Clear();
    }

    private bool TryAttach(Entity<CFWallmountComponent> wallmount)
    {
        var xform = Transform(wallmount);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var offset = xform.LocalRotation.ToWorldVec().Normalized();
        var target = new EntityCoordinates(grid, xform.LocalPosition - offset);

        foreach (var anchored in _map.GetAnchoredEntities(grid, gridComp, target))
        {
            if (!_tag.HasAnyTag(anchored, WallTags))
                continue;

            EnsureComp<CFWallmountedComponent>(anchored).Attached.Add(wallmount);
            return true;
        }

        return false;
    }
}
