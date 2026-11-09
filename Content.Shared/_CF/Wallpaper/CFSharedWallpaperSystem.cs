using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.CF.Wallpaper;

/// <summary>Hanging wallpaper. Abstract with a subclass on each side, so its subscriptions are made once per side.</summary>
public abstract partial class CFSharedWallpaperSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedStackSystem _stack = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CFWallpaperHolderComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CFWallpaperHolderComponent, CFWallpaperAddLayerDoAfterEvent>(OnAddDoAfter);
    }

    private void OnAddDoAfter(Entity<CFWallpaperHolderComponent> holder, ref CFWallpaperAddLayerDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target == null)
            return;

        if (!TryComp<CFWallpaperComponent>(args.Used, out var wallpaper))
            return;

        var userPosition = _transform.GetWorldPosition(args.User);
        var holderPosition = _transform.GetWorldPosition(holder);
        var deltaX = holderPosition.X - userPosition.X;
        var deltaY = holderPosition.Y - userPosition.Y;

        var direction = Math.Abs(deltaX) > Math.Abs(deltaY)
            ? deltaX > 0 ? wallpaper.Right : wallpaper.Left
            : deltaY > 0 ? wallpaper.Bottom : wallpaper.Top;

        holder.Comp.Layers.Add(new SpriteSpecifier.Rsi(new ResPath(wallpaper.RsiPath), direction));
        Dirty(holder);

        if (TryComp<StackComponent>(args.Used, out var stack))
            _stack.SetCount(args.Used.Value, stack.Count - 1, stack);
        else
            QueueDel(args.Used);
    }

    private void OnInteractUsing(Entity<CFWallpaperHolderComponent> holder, ref InteractUsingEvent args)
    {
        if (!TryComp<CFWallpaperComponent>(args.Used, out var wallpaper))
            return;

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            wallpaper.Delay,
            new CFWallpaperAddLayerDoAfterEvent(),
            holder,
            holder,
            args.Used)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            MovementThreshold = 0.5f,
            CancelDuplicate = false,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }
}

[Serializable, NetSerializable]
public sealed partial class CFWallpaperAddLayerDoAfterEvent : SimpleDoAfterEvent;
