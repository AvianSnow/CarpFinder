// Ported from CrystallEdge's z-level system (https://github.com/crystallpunk-14/crystall-edge),
// with permission from eshhhed. CrystallEdge sublicenses it under the MIT License.
// CarpFinder changes: CF names and namespaces, and whatever each file's comments say.

using Content.Shared.CF.ZLevels.Core.EntitySystems;
using Robust.Shared.Analyzers;

namespace Content.Shared.CF.ZLevels.Ghost;

public abstract partial class CFSharedZLevelGhostMoverSystem : EntitySystem
{
    [Dependency] private CFSharedZLevelsSystem _zLevel = null!;

    [SubscribeLocalEvent]
    private void OnZLevelDown(Entity<CFZLevelGhostMoverComponent> ent, ref CFZLevelActionDown args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveDown(ent);
    }

    [SubscribeLocalEvent]
    private void OnZLevelUp(Entity<CFZLevelGhostMoverComponent> ent, ref CFZLevelActionUp args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveUp(ent);
    }
}
