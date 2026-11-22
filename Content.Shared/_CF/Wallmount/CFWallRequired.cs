// Ported from CrystallPunk 14 (CP14WallRequired).
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.CF.Wallmount;

/// <summary>
/// Construction condition: there must be a wall or window behind the placed entity.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class CFWallRequired : IConstructionCondition
{
    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = "cp14-construction-step-condition-wall-required",
        };
    }

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entityManager = IoCManager.Resolve<IEntityManager>();
        var mapSystem = entityManager.System<SharedMapSystem>();
        var transformSystem = entityManager.System<SharedTransformSystem>();
        var tagSystem = entityManager.System<TagSystem>();

        var grid = transformSystem.GetGrid(user);
        if (grid == null || !entityManager.TryGetComponent<MapGridComponent>(grid, out var gridComp))
            return false;

        var target = location.Offset(-direction.ToAngle().ToWorldVec());
        foreach (var anchored in mapSystem.GetAnchoredEntities(grid.Value, gridComp, target))
        {
            if (CFWallmountSystem.IsWall(entityManager, tagSystem, anchored))
                return true;
        }

        return false;
    }
}
