using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client.CF.Hud;

/// <summary>A horizontal bar with its value written on it, for hit points and temporary hit points.</summary>
public sealed class CFHudBar : Control
{
    private const string FontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private static readonly Color Track = new(12, 12, 16, 230);
    private static readonly Color Edge = new(96, 96, 104, 220);

    private readonly Font _font;
    private float _target;
    private float _shown;
    private bool _ready;
    private string _text = string.Empty;

    public Color Fill { get; set; } = Color.Red;

    public CFHudBar()
    {
        _font = new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>(FontPath), 12);
        MouseFilter = MouseFilterMode.Stop;
    }

    /// <param name="fraction">How full, from 0 to 1.</param>
    /// <param name="text">What to write on it.</param>
    public void Set(float fraction, string text)
    {
        _target = Math.Clamp(fraction, 0f, 1f);
        _text = text;
        if (_ready)
            return;

        _shown = _target;
        _ready = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _shown += (_target - _shown) * Math.Min(1f, args.DeltaSeconds * 8f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        handle.DrawRect(box, Track);
        if (_shown > 0f)
            handle.DrawRect(new UIBox2(0f, 0f, box.Width * _shown, box.Height), Fill);
        handle.DrawRect(box, Edge, filled: false);

        var size = handle.GetDimensions(_font, _text, UIScale);
        var at = new Vector2(6f * UIScale, (box.Height - size.Y) / 2f);
        handle.DrawString(_font, at, _text, UIScale, Color.White);
    }
}
