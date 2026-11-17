// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.Components;
using Content.Shared.Throwing;
using Robust.Shared.Analyzers;

namespace Content.Shared.CF.ZLevels.Throwing;

/// <summary>
/// Keeps z-physics out of the way of a vanilla throw: while an entity is actually being
/// thrown, its horizontal flight distance/timing is fully governed by ThrowingSystem's
/// friction-based model, so z-physics gravity/ground-sync/BodyStatus-sync must not run
/// for it (that fight is what caused throws to land short or overshoot the cursor).
/// </summary>
public sealed partial class CFZLevelThrowingSystem : EntitySystem
{

    [SubscribeLocalEvent]
    private void OnThrown(Entity<CFZPhysicsComponent> ent, ref ThrownEvent args)
    {
        ent.Comp.Disabled = true;
        DirtyField(ent, ent.Comp, nameof(CFZPhysicsComponent.Disabled));
    }

    [SubscribeLocalEvent]
    private void OnStopThrow(Entity<CFZPhysicsComponent> ent, ref StopThrowEvent args)
    {
        ent.Comp.Disabled = false;
        DirtyField(ent, ent.Comp, nameof(CFZPhysicsComponent.Disabled));
    }
}
