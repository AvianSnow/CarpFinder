using Content.Shared.CF.Farming;
using Content.Shared.Rounding;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client.CF.Farming;

/// <summary>Shows the grow-N sprite state that matches a plant's growth.</summary>
public sealed partial class CFPlantVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<CFPlantComponent> ent, ref ComponentStartup args)
    {
        UpdateSprite(ent);
    }

    [SubscribeLocalEvent]
    private void OnState(Entity<CFPlantComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite(ent);
    }

    private void UpdateSprite(Entity<CFPlantComponent> ent)
    {
        if (!TryComp(ent, out SpriteComponent? sprite)
            || !_sprite.LayerMapTryGet((ent, sprite), CFPlantVisualLayers.Base, out var layer, false))
        {
            return;
        }

        var step = Math.Max(1, ContentHelpers.RoundToNearestLevels(ent.Comp.GrowthLevel, 1, ent.Comp.GrowthSteps));
        _sprite.LayerSetRsiState((ent, sprite), layer, $"grow-{step}");
    }
}
