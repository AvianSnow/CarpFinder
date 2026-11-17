// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.Components;
using JetBrains.Annotations;
using Robust.Shared.Analyzers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Shared.CF.ZLevels.Core.EntitySystems;

public abstract partial class CFSharedZLevelsSystem
{
    private readonly List<EntityUid> _activeBodies = new();

    public IReadOnlyList<EntityUid> ActiveBodies => _activeBodies;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFZPhysicsComponent> entity, ref MapInitEvent args)
    {
        // CarpFinder: a body spawned over open air must know its ground at once. Its first move event came before
        // this component could hear it, so without this it would stand on nothing until something moved it.
        RequestCacheMovement(entity);
        RefreshBody(entity);
        SyncCurrentZLevel(entity);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<CFZPhysicsComponent> entity, ref ComponentShutdown args)
    {
        SleepBody((entity, entity));
    }

    [SubscribeLocalEvent]
    private void OnAnchorStateChanged(Entity<CFZPhysicsComponent> entity, ref AnchorStateChangedEvent args)
    {
        RefreshBody(entity);
    }

    [SubscribeLocalEvent]
    private void OnPhysicsBodyTypeChanged(Entity<CFZPhysicsComponent> entity, ref PhysicsBodyTypeChangedEvent args)
    {
        RefreshBody(entity);
    }

    [SubscribeLocalEvent]
    private void OnParentChanged(Entity<CFZPhysicsComponent> entity, ref EntParentChangedMessage args)
    {
        RefreshBody(entity);
        SyncCurrentZLevel(entity);

        if (ZPhysicsQuery.TryComp(args.OldParent, out var oldParentPhysics))
        {
            SetZPosition((entity, entity), oldParentPhysics.LocalPosition);
            return;
        }

        var xform = Transform(entity);
        if (!IsLoose(xform))
            ResetZState(entity);
    }

    /// <summary>
    /// Clears height and velocity without waking the body: nested bodies are moved by their parent.
    /// </summary>
    private void ResetZState(Entity<CFZPhysicsComponent> entity)
    {
        if (entity.Comp.LocalPosition != 0)
        {
            entity.Comp.LocalPosition = 0;
            DirtyField(entity, entity.Comp, nameof(CFZPhysicsComponent.LocalPosition));
        }

        if (entity.Comp.Velocity != 0)
        {
            entity.Comp.Velocity = 0;
            DirtyField(entity, entity.Comp, nameof(CFZPhysicsComponent.Velocity));
        }
    }

    /// <summary>
    /// Only unanchored, non-static bodies parented directly to the grid or the map simulate z-physics.
    /// </summary>
    private bool CanBeActive(EntityUid uid, TransformComponent xform)
    {
        if (!IsLoose(xform) || xform.Anchored)
            return false;

        return !_physicsQuery.TryComp(uid, out var physics) || physics.BodyType != BodyType.Static;
    }

    /// <summary>
    /// CarpFinder: a body that stands on its own, parented to its grid or, over open sky beside a grid, to the map.
    /// CrystallEdge's levels are planet maps, where map and grid are one entity, so it only asked for the grid.
    /// Our islands are grids inside a map, and a creature that steps off one is parented to the map and must still fall.
    /// Anything else (in a container, buckled, riding) travels with its parent.
    /// </summary>
    public static bool IsLoose(TransformComponent xform)
    {
        return xform.ParentUid == xform.GridUid || xform.ParentUid == xform.MapUid;
    }

    private void SyncCurrentZLevel(Entity<CFZPhysicsComponent> entity)
    {
        if (!_zMapQuery.TryComp(Transform(entity).MapUid, out var zLevel))
            return;

        if (entity.Comp.CurrentZLevel == zLevel.Depth)
            return;

        entity.Comp.CurrentZLevel = zLevel.Depth;
        DirtyField(entity, entity.Comp, nameof(CFZPhysicsComponent.CurrentZLevel));
    }

    [PublicAPI]
    public void WakeBody(Entity<CFZPhysicsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        if (_activeBodies.Contains(entity))
            return;

        if (!_transformQuery.TryComp(entity, out var xform) || !CanBeActive(entity, xform))
            return;

        entity.Comp.Sleeping = false;
        entity.Comp.SleepTimer = 0f;

        _activeBodies.Add(entity);
    }

    [PublicAPI]
    public void SleepBody(Entity<CFZPhysicsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        entity.Comp.Sleeping = true;
        entity.Comp.SleepTimer = 0f;

        _activeBodies.Remove(entity);
    }

    [PublicAPI]
    public void RefreshBody(Entity<CFZPhysicsComponent> entity)
    {
        if (TerminatingOrDeleted(entity))
        {
            SleepBody((entity, entity));
            return;
        }

        if (!CanBeActive(entity, Transform(entity)))
        {
            SleepBody((entity, entity));
            return;
        }

        WakeBody((entity, entity));
    }
}
