// Ported from CrystallPunk 14 (CP14NoRoofInTile).
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.CF.Roof;

/// <summary>
/// Construction condition: the target tile must not already hold a roof.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class CFNoRoofInTile : IConstructionCondition
{
    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = "cp14-construction-step-condition-no-roof-in-tile",
        };
    }

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entityManager = IoCManager.Resolve<IEntityManager>();
        var mapSystem = entityManager.System<SharedMapSystem>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        var grid = transformSystem.GetGrid(user);
        if (grid == null || !entityManager.TryGetComponent<MapGridComponent>(grid, out var gridComp))
            return false;

        var targetPos = transformSystem.ToMapCoordinates(location);
        foreach (var anchored in mapSystem.GetAnchoredEntities(grid.Value, gridComp, targetPos))
        {
            if (entityManager.HasComponent<CFRoofComponent>(anchored))
                return false;
        }

        return true;
    }
}
