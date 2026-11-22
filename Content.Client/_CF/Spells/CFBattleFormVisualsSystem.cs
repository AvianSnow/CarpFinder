using Content.Shared.CF.Spells;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.CF.Spells;

/// <summary>The layers this client hid on a creature in a battle form, to show again when the form ends.</summary>
[RegisterComponent]
public sealed partial class CFBattleFormHiddenComponent : Component
{
    public List<SpriteComponent.Layer> Hidden = new();

    public SpriteSpecifier.Rsi? Shown;
}

/// <summary>
/// Draws a creature in a battle form as the animal it has become. The body, clothes and held items are layers of one
/// sprite, so the form is one more layer and every other layer is hidden while it lasts. Clothes put on during the form add
/// layers of their own, which is why this looks again each frame.
/// </summary>
public sealed partial class CFBattleFormVisualsSystem : EntitySystem
{
    private const string LayerKey = "cf-battle-form";

    [Dependency] private SpriteSystem _sprite = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<CFBattleFormComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var form, out var sprite))
        {
            if (form.Sprite != null)
                Show((uid, sprite), form.Sprite);
        }
    }

    private void Show(Entity<SpriteComponent> ent, SpriteSpecifier.Rsi shape)
    {
        var hidden = EnsureComp<CFBattleFormHiddenComponent>(ent);
        Entity<SpriteComponent?> sprite = (ent.Owner, ent.Comp);
        if (!_sprite.LayerMapTryGet(sprite, LayerKey, out var index, false))
        {
            index = _sprite.AddLayer(sprite, shape);
            _sprite.LayerMapSet(sprite, LayerKey, index);
            hidden.Shown = shape;
        }
        else if (!Equals(hidden.Shown, shape))
        {
            // One form straight into another.
            _sprite.LayerSetSprite(sprite, index, shape);
            hidden.Shown = shape;
        }

        if (!_sprite.TryGetLayer(sprite, index, out var own, false))
            return;

        _sprite.LayerSetVisible(own, true);
        foreach (var other in ent.Comp.AllLayers)
        {
            if (other is not SpriteComponent.Layer layer || layer == own || !layer.Visible)
                continue;

            _sprite.LayerSetVisible(layer, false);
            hidden.Hidden.Add(layer);
        }
    }

    [SubscribeLocalEvent]
    private void OnFormEnded(Entity<CFBattleFormComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp(ent, out CFBattleFormHiddenComponent? hidden))
            return;

        if (TryComp(ent, out SpriteComponent? sprite))
        {
            _sprite.RemoveLayer((ent.Owner, sprite), LayerKey, false);
            foreach (var layer in hidden.Hidden)
            {
                _sprite.LayerSetVisible(layer, true);
            }
        }

        RemComp<CFBattleFormHiddenComponent>(ent);
    }
}
