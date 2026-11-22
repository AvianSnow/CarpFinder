// Ported from Crystall Punk 14 (CP14ClientCookingSystem), MIT.
using Content.Client.DisplacementMap;
using Content.Shared.CF.Cooking;
using Content.Shared.DisplacementMap;
using Content.Shared.Rounding;
using Robust.Client.GameObjects;

namespace Content.Client.CF.Cooking;

/// <summary>Draws a dish's layers on whatever holds it, bent to the shape of the pot or plate.</summary>
public sealed partial class CFClientCookerSystem : CFSharedCookerSystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private DisplacementMapSystem _displacement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CFFoodHolderComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
        SubscribeLocalEvent<CFFoodHolderComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(Entity<CFFoodHolderComponent> ent, ref AppearanceChangeEvent args)
    {
        UpdateVisuals(ent);
    }

    private void OnAfterHandleState(Entity<CFFoodHolderComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateVisuals(ent);
    }

    private void UpdateVisuals(Entity<CFFoodHolderComponent> ent)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        foreach (var key in ent.Comp.RevealedLayers)
        {
            _sprite.RemoveLayer((ent, sprite), key, false);
        }

        ent.Comp.RevealedLayers.Clear();

        if (ent.Comp.FoodData is null
            || !Solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out _, out var solution)
            || !_sprite.LayerMapTryGet((ent, sprite), ent.Comp.TargetLayerMap, out var index, false))
        {
            return;
        }

        var fill = solution.MaxVolume > 0 ? Math.Min(1f, (float) solution.Volume / (float) solution.MaxVolume) : 0f;
        var level = ContentHelpers.RoundToLevels(fill, 1, ent.Comp.MaxDisplacementFillLevels + 1);

        var counter = 0;
        foreach (var layer in ent.Comp.FoodData.Visuals)
        {
            var layerIndex = index + counter;
            var key = $"cf-food-layer-{counter}";
            ent.Comp.RevealedLayers.Add(key);

            _sprite.AddBlankLayer((ent, sprite), layerIndex);
            _sprite.LayerMapSet((ent, sprite), key, layerIndex);
            _sprite.LayerSetData((ent, sprite), layerIndex, layer);

            if (ent.Comp.DisplacementRsiPath is { } rsi)
            {
                var displacement = new DisplacementData
                {
                    SizeMaps = new Dictionary<int, PrototypeLayerData>
                    {
                        { 32, new PrototypeLayerData { RsiPath = rsi, State = "fill-" + level } },
                    },
                };

                if (_displacement.TryAddDisplacement(displacement, (ent, sprite), layerIndex, key, out var displacementKey))
                    ent.Comp.RevealedLayers.Add(displacementKey);
            }

            counter++;
        }
    }
}
