using Content.Client.IconSmoothing;
using Content.Shared.CF.Wallpaper;
using Content.Shared.IconSmoothing;
using Robust.Client.GameObjects;

namespace Content.Client.CF.Wallpaper;

public sealed class CFClientWallpaperSystem : CFSharedWallpaperSystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<CFWallpaperHolderComponent, AfterAutoHandleStateEvent>(
            OnHandleState,
            after: [typeof(IconSmoothSystem), typeof(SharedRandomIconSmoothSystem), typeof(ClientRandomIconSmoothSystem)]);
        SubscribeLocalEvent<CFWallpaperHolderComponent, ComponentStartup>(
            OnStartup,
            after: [typeof(IconSmoothSystem), typeof(SharedRandomIconSmoothSystem), typeof(ClientRandomIconSmoothSystem)]);
    }

    private void OnStartup(Entity<CFWallpaperHolderComponent> holder, ref ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(holder, out var sprite))
            UpdateVisuals(holder, sprite);
    }

    private void OnHandleState(Entity<CFWallpaperHolderComponent> holder, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp<SpriteComponent>(holder, out var sprite))
            UpdateVisuals(holder, sprite);
    }

    private static void UpdateVisuals(Entity<CFWallpaperHolderComponent> holder, SpriteComponent sprite)
    {
        foreach (var key in holder.Comp.RevealedLayers)
            sprite.RemoveLayer(key);

        holder.Comp.RevealedLayers.Clear();

        var counter = 0;
        foreach (var wallpaper in holder.Comp.Layers)
        {
            var keyCode = $"wallpaper-layer-{counter++}";
            holder.Comp.RevealedLayers.Add(keyCode);

            var index = sprite.LayerMapReserveBlank(keyCode);
            sprite.LayerSetSprite(index, wallpaper);
        }
    }
}
