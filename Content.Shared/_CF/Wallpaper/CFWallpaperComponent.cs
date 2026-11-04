namespace Content.Shared.CF.Wallpaper;

[RegisterComponent, Access(typeof(CFSharedWallpaperSystem))]
public sealed partial class CFWallpaperComponent : Component
{
    [DataField]
    public float Delay = 1f;

    [DataField(required: true)]
    public string RsiPath = default!;

    [DataField]
    public string Bottom = "bottom";

    [DataField]
    public string Top = "top";

    [DataField]
    public string Left = "left";

    [DataField]
    public string Right = "right";
}
