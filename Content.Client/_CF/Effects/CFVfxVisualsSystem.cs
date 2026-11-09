using Content.Shared.CF.Effects;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client.CF.Effects;

/// <summary>Tints a magic effect with the colour the server chose for it.</summary>
public sealed partial class CFVfxVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<CFVfxComponent> ent, ref ComponentStartup args)
    {
        Tint(ent);
    }

    [SubscribeLocalEvent]
    private void OnState(Entity<CFVfxComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Tint(ent);
    }

    private void Tint(Entity<CFVfxComponent> ent)
    {
        if (TryComp(ent, out SpriteComponent? sprite))
            _sprite.SetColor((ent, sprite), ent.Comp.Color);
    }
}
