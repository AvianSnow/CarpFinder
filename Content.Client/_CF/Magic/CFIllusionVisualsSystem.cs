using Content.Shared.CF.Magic;
using Robust.Client.GameObjects;

namespace Content.Client.CF.Magic;

/// <summary>Gives an illusion its pale, see-through look.</summary>
public sealed partial class CFIllusionVisualsSystem : EntitySystem
{
    private static readonly Color Ghostly = new(0.6f, 0.8f, 1f, 0.55f);

    [Dependency] private SpriteSystem _sprite = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<CFIllusionComponent> ent, ref ComponentStartup args)
    {
        if (TryComp(ent, out SpriteComponent? sprite))
            _sprite.SetColor((ent, sprite), Ghostly);
    }
}
