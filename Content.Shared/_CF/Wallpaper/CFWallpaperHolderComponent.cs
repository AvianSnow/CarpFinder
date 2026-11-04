using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared.CF.Wallpaper;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), Access(typeof(CFSharedWallpaperSystem))]
public sealed partial class CFWallpaperHolderComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<SpriteSpecifier> Layers = new();

    public HashSet<string> RevealedLayers = new();

    [DataField]
    public int MaxLayers = 4;
}
